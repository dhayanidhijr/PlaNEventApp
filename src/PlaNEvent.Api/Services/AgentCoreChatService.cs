using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PlaNEvent.Shared.Contracts;

namespace PlaNEvent.Api.Services;

public interface IAgentCoreChatService
{
    Task<AgentChatResponse> ChatAsync(AgentChatRequest request, CancellationToken cancellationToken);
    Task StreamChatAsync(AgentChatRequest request, HttpResponse response, CancellationToken cancellationToken);
}

public sealed class AgentCoreChatService(
    IAmazonBedrockAgentCore bedrockClient,
    IOptions<AgentCoreOptions> options,
    IHttpContextAccessor httpContextAccessor) : IAgentCoreChatService
{
    private readonly AgentCoreOptions optionsValue = options.Value;

    public async Task<AgentChatResponse> ChatAsync(AgentChatRequest request, CancellationToken cancellationToken)
    {
        var sessionId = NormalizeSessionId(request.SessionId);

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return new AgentChatResponse { SessionId = sessionId, Reply = "Please provide a message.", HtmlReply = "<p>Please provide a message.</p>" };
        }

        if (string.IsNullOrWhiteSpace(optionsValue.AgentRuntimeArn))
        {
            const string message = "AgentCore runtime ARN is not configured. Set AgentCore:AgentRuntimeArn in API settings.";
            return new AgentChatResponse
            {
                SessionId = sessionId,
                Reply = message,
                HtmlReply = $"<p>{WebUtility.HtmlEncode(message)}</p>"
            };
        }

        try
        {
            var firstPass = await InvokeAndExtractAsync(request.Message, sessionId, cancellationToken);
            var answerText = string.IsNullOrWhiteSpace(firstPass.Reply) ? "No response from agent." : firstPass.Reply;
            var html = FormatReplyAsHtml(answerText);

            return new AgentChatResponse
            {
                SessionId = firstPass.SessionId,
                Reply = answerText,
                HtmlReply = html
            };
        }
        catch (Exception ex)
        {
            var message = $"AgentCore call failed: {ex.Message}";
            return new AgentChatResponse
            {
                SessionId = sessionId,
                Reply = message,
                HtmlReply = $"<p>{WebUtility.HtmlEncode(message)}</p>"
            };
        }
    }

    public async Task StreamChatAsync(AgentChatRequest request, HttpResponse response, CancellationToken cancellationToken)
    {
        var sessionId = NormalizeSessionId(request.SessionId);

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Append("X-Accel-Buffering", "no");

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            await WriteSseEventAsync(response, "error", new
            {
                sessionId,
                message = "Please provide a message."
            }, cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(optionsValue.AgentRuntimeArn))
        {
            await WriteSseEventAsync(response, "error", new
            {
                sessionId,
                message = "AgentCore runtime ARN is not configured. Set AgentCore:AgentRuntimeArn in API settings."
            }, cancellationToken);
            return;
        }

        try
        {
            var streamResult = await StreamFromRuntimeAsync(request.Message, sessionId, response, cancellationToken);
            var reply = string.IsNullOrWhiteSpace(streamResult.Reply) ? "No response from agent." : streamResult.Reply;
            var htmlReply = FormatReplyAsHtml(reply);

            await WriteSseEventAsync(response, "complete", new { sessionId = streamResult.SessionId, reply, htmlReply }, cancellationToken);
        }
        catch (Exception)
        {
            try
            {
                var result = await InvokeAndExtractAsync(request.Message, sessionId, cancellationToken);
                var reply = string.IsNullOrWhiteSpace(result.Reply) ? "No response from agent." : result.Reply;
                var htmlReply = FormatReplyAsHtml(reply);

                await WriteSseEventAsync(response, "session", new { sessionId = result.SessionId }, cancellationToken);
                foreach (var chunk in ChunkTextForStream(reply))
                {
                    await WriteSseEventAsync(response, "delta", new { sessionId = result.SessionId, delta = chunk }, cancellationToken);
                }

                await WriteSseEventAsync(response, "complete", new { sessionId = result.SessionId, reply, htmlReply }, cancellationToken);
            }
            catch (Exception ex)
            {
                await WriteSseEventAsync(response, "error", new
                {
                    sessionId,
                    message = $"AgentCore call failed: {ex.Message}"
                }, cancellationToken);
            }
        }
    }

    private async Task<(string SessionId, string Reply)> InvokeAndExtractAsync(string prompt, string sessionId, CancellationToken cancellationToken)
    {
        var payloadJson = BuildPayloadJson(prompt, sessionId, stream: false);

        using var payloadStream = new MemoryStream(Encoding.UTF8.GetBytes(payloadJson));

        var invokeRequest = new InvokeAgentRuntimeRequest
        {
            AgentRuntimeArn = optionsValue.AgentRuntimeArn,
            Qualifier = optionsValue.Qualifier,
            ContentType = "application/json",
            Accept = "application/json",
            RuntimeSessionId = sessionId,
            Payload = payloadStream
        };

        var response = await bedrockClient.InvokeAgentRuntimeAsync(invokeRequest, cancellationToken);
        var responseText = await ReadResponseAsync(response);

        return (response.RuntimeSessionId ?? sessionId, ExtractReply(responseText));
    }

    private string BuildPayloadJson(string prompt, string sessionId, bool stream)
    {
        var bearerToken = GetForwardedAccessToken();
        return JsonSerializer.Serialize(new
        {
            sessionId,
            prompt,
            message = prompt,
            inputText = prompt,
            stream,
            accessToken = bearerToken,
            apiBaseUrl = optionsValue.ApiBaseUrl,
            swaggerUrl = optionsValue.SwaggerUrl,
            userContext = new
            {
                email = httpContextAccessor.HttpContext?.User?.Identity?.Name,
                roles = httpContextAccessor.HttpContext?.User?.Claims
                    .Where(claim => claim.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase) || claim.Type == "role")
                    .Select(claim => claim.Value)
                    .ToArray() ?? Array.Empty<string>()
            }
        });
    }

    private string? GetForwardedAccessToken()
    {
        if (!optionsValue.ForwardUserToken)
        {
            return null;
        }

        var authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return null;
        }

        const string bearerPrefix = "Bearer ";
        return authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[bearerPrefix.Length..].Trim()
            : authorization.Trim();
    }

    private static string FormatReplyAsHtml(string answerText)
    {
        if (string.IsNullOrWhiteSpace(answerText))
        {
            return "<p><em>No response from agent.</em></p>";
        }

        var lines = answerText.Replace("\r\n", "\n").Split('\n');
        var html = new StringBuilder();
        var paragraph = new List<string>();
        var unorderedList = new List<string>();
        var orderedList = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0)
            {
                return;
            }

            var text = string.Join(' ', paragraph).Trim();
            if (text.Length > 0)
            {
                html.Append("<p>")
                    .Append(ApplyInlineFormatting(text))
                    .Append("</p>");
            }

            paragraph.Clear();
        }

        void FlushUnorderedList()
        {
            if (unorderedList.Count == 0)
            {
                return;
            }

            html.Append("<ul>");
            foreach (var item in unorderedList)
            {
                html.Append("<li>")
                    .Append(ApplyInlineFormatting(item))
                    .Append("</li>");
            }

            html.Append("</ul>");
            unorderedList.Clear();
        }

        void FlushOrderedList()
        {
            if (orderedList.Count == 0)
            {
                return;
            }

            html.Append("<ol>");
            foreach (var item in orderedList)
            {
                html.Append("<li>")
                    .Append(ApplyInlineFormatting(item))
                    .Append("</li>");
            }

            html.Append("</ol>");
            orderedList.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                FlushUnorderedList();
                FlushOrderedList();
                continue;
            }

            if (TryParseListItem(line, out var itemText, out var isOrdered))
            {
                FlushParagraph();
                if (isOrdered)
                {
                    FlushUnorderedList();
                    orderedList.Add(itemText);
                }
                else
                {
                    FlushOrderedList();
                    unorderedList.Add(itemText);
                }

                continue;
            }

            FlushUnorderedList();
            FlushOrderedList();
            paragraph.Add(line);
        }

        FlushParagraph();
        FlushUnorderedList();
        FlushOrderedList();

        return html.Length == 0
            ? $"<p>{ApplyInlineFormatting(answerText.Trim())}</p>"
            : html.ToString();
    }

    private static async Task<string> ReadResponseAsync(InvokeAgentRuntimeResponse response)
    {
        if (response.Response is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(response.Response);
        return await reader.ReadToEndAsync();
    }

    private static string ExtractReply(string payload)
    {
        return ExtractReply(payload, 0);
    }

    private async Task<(string SessionId, string Reply)> StreamFromRuntimeAsync(
        string prompt,
        string sessionId,
        HttpResponse downstreamResponse,
        CancellationToken cancellationToken)
    {
        var payloadJson = BuildPayloadJson(prompt, sessionId, stream: true);
        using var payloadStream = new MemoryStream(Encoding.UTF8.GetBytes(payloadJson));
        var invokeRequest = new InvokeAgentRuntimeRequest
        {
            AgentRuntimeArn = optionsValue.AgentRuntimeArn,
            Qualifier = optionsValue.Qualifier,
            ContentType = "application/json",
            Accept = "text/event-stream",
            RuntimeSessionId = sessionId,
            Payload = payloadStream
        };

        var runtimeResponse = await bedrockClient.InvokeAgentRuntimeAsync(invokeRequest, cancellationToken);

        var actualSessionId = runtimeResponse.RuntimeSessionId ?? sessionId;
        await WriteSseEventAsync(downstreamResponse, "session", new { sessionId = actualSessionId }, cancellationToken);

        if (runtimeResponse.Response is null)
        {
            throw new InvalidOperationException("Runtime stream response was empty.");
        }

        await using var stream = runtimeResponse.Response;
        using var reader = new StreamReader(stream);

        var sseBuffer = new StringBuilder();
        var replyBuffer = new StringBuilder();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
            if (string.IsNullOrEmpty(line))
            {
                if (sseBuffer.Length == 0)
                {
                    continue;
                }

                var completion = await ProcessRuntimeStreamPayloadAsync(
                    sseBuffer.ToString(),
                    actualSessionId,
                    replyBuffer,
                    downstreamResponse,
                    cancellationToken);
                sseBuffer.Clear();
                if (completion is not null)
                {
                    return (actualSessionId, completion);
                }

                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                sseBuffer.AppendLine(line["data:".Length..].Trim());
            }
            else
            {
                sseBuffer.AppendLine(line.Trim());
            }
        }

        if (sseBuffer.Length > 0)
        {
            var completion = await ProcessRuntimeStreamPayloadAsync(
                sseBuffer.ToString(),
                actualSessionId,
                replyBuffer,
                downstreamResponse,
                cancellationToken);
            if (completion is not null)
            {
                return (actualSessionId, completion);
            }
        }

        return (actualSessionId, replyBuffer.ToString().Trim());
    }

    private static bool TryParseListItem(string line, out string itemText, out bool isOrdered)
    {
        var unorderedMatch = Regex.Match(line, @"^(?:[-*•]\s+)(.+)$");
        if (unorderedMatch.Success)
        {
            itemText = unorderedMatch.Groups[1].Value.Trim();
            isOrdered = false;
            return true;
        }

        var orderedMatch = Regex.Match(line, @"^(?:\d+[\.\)]\s+)(.+)$");
        if (orderedMatch.Success)
        {
            itemText = orderedMatch.Groups[1].Value.Trim();
            isOrdered = true;
            return true;
        }

        itemText = string.Empty;
        isOrdered = false;
        return false;
    }

    private static string ApplyInlineFormatting(string text)
    {
        var encoded = WebUtility.HtmlEncode(text);
        encoded = Regex.Replace(encoded, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        encoded = Regex.Replace(encoded, @"\*(.+?)\*", "<em>$1</em>");
        encoded = Regex.Replace(encoded, @"`(.+?)`", "<code>$1</code>");
        encoded = Regex.Replace(
            encoded,
            @"(https?://[^\s<]+)",
            match => $"<a href=\"{match.Value}\" target=\"_blank\" rel=\"noopener noreferrer\">{match.Value}</a>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return encoded;
    }

    private static string NormalizeSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return $"session{Guid.NewGuid():N}";
        }

        var normalized = sessionId.Trim();
        if (normalized.Length < 33)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
            normalized += hash[..(33 - normalized.Length)];
        }

        if (normalized.Length > 100)
        {
            normalized = normalized[..100];
        }

        return normalized;
    }

    private static async Task<string?> ProcessRuntimeStreamPayloadAsync(
        string payload,
        string sessionId,
        StringBuilder replyBuffer,
        HttpResponse downstreamResponse,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var streamedEvent = ParseRuntimeStreamEvent(payload, sessionId);
        switch (streamedEvent.Kind)
        {
            case "delta":
                if (!string.IsNullOrWhiteSpace(streamedEvent.Delta))
                {
                    replyBuffer.Append(streamedEvent.Delta);
                    await WriteSseEventAsync(downstreamResponse, "delta", new
                    {
                        sessionId,
                        delta = streamedEvent.Delta,
                        htmlReply = FormatReplyAsHtml(replyBuffer.ToString())
                    }, cancellationToken);
                }

                return null;
            case "complete":
                if (!string.IsNullOrWhiteSpace(streamedEvent.Reply))
                {
                    replyBuffer.Clear();
                    replyBuffer.Append(streamedEvent.Reply);
                }

                return replyBuffer.ToString().Trim();
            case "error":
                throw new InvalidOperationException(streamedEvent.Message ?? "Runtime stream failed.");
            default:
                return null;
        }
    }

    private static (string Kind, string? Delta, string? Reply, string? Message) ParseRuntimeStreamEvent(string payload, string sessionId)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var kind = root.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString() ?? string.Empty
            : string.Empty;

        return kind switch
        {
            "session" => ("session", null, null, null),
            "delta" => ("delta", root.TryGetProperty("delta", out var delta) ? delta.GetString() : null, null, null),
            "complete" => ("complete", null, root.TryGetProperty("reply", out var reply) ? reply.GetString() : null, null),
            "error" => ("error", null, null, root.TryGetProperty("message", out var message) ? message.GetString() : "Runtime stream failed."),
            _ => ("delta", ExtractReply(payload), null, null)
        };
    }

    private static string ExtractReply(string payload, int depth)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        if (depth >= 4)
        {
            return payload.Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (TryGetString(root, "outputText", out var outputText)) return ExtractReply(outputText, depth + 1);
            if (TryGetString(root, "response", out var response)) return ExtractReply(response, depth + 1);
            if (TryGetString(root, "message", out var message)) return ExtractReply(message, depth + 1);
            if (TryGetString(root, "completion", out var completion)) return ExtractReply(completion, depth + 1);

            if (root.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.Array
                && content.GetArrayLength() > 0)
            {
                var first = content[0];
                if (TryGetString(first, "text", out var text)) return ExtractReply(text, depth + 1);
                if (first.ValueKind == JsonValueKind.String) return ExtractReply(first.GetString() ?? payload, depth + 1);
            }

            if (root.TryGetProperty("result", out var result))
            {
                if (TryGetString(result, "outputText", out var resultOutput)) return ExtractReply(resultOutput, depth + 1);
                if (TryGetString(result, "response", out var resultResponse)) return ExtractReply(resultResponse, depth + 1);
                if (TryGetString(result, "message", out var resultMessage)) return ExtractReply(resultMessage, depth + 1);

                if (result.TryGetProperty("content", out var resultContent)
                    && resultContent.ValueKind == JsonValueKind.Array
                    && resultContent.GetArrayLength() > 0)
                {
                    foreach (var item in resultContent.EnumerateArray())
                    {
                        if (TryGetString(item, "text", out var resultText)) return ExtractReply(resultText, depth + 1);
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            return ExtractReply(item.GetString() ?? payload, depth + 1);
                        }
                    }
                }
            }
        }
        catch
        {
            if (TryExtractPseudoJsonText(payload, out var pseudoJsonText))
            {
                return ExtractReply(pseudoJsonText, depth + 1);
            }
        }

        return payload.Trim();
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryExtractPseudoJsonText(string payload, out string value)
    {
        var matches = Regex.Matches(payload, @"['""]text['""]\s*:\s*(?<quote>['""])(?<value>(?:\\.|(?!\k<quote>).)*)\k<quote>");
        if (matches.Count == 0)
        {
            value = string.Empty;
            return false;
        }

        var texts = matches
            .Select(match => Regex.Unescape(match.Groups["value"].Value).Trim())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        value = texts.Length == 0 ? string.Empty : string.Join("\n\n", texts);
        return texts.Length > 0;
    }

    private static async Task WriteSseEventAsync(HttpResponse response, string eventName, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        await response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static IReadOnlyList<string> ChunkTextForStream(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var sentenceChunks = Regex.Split(text.Trim(), @"(?<=[.!?])\s+")
            .Where(chunk => !string.IsNullOrWhiteSpace(chunk))
            .ToArray();

        if (sentenceChunks.Length > 1)
        {
            return sentenceChunks
                .Select((chunk, index) => index < sentenceChunks.Length - 1 ? $"{chunk} " : chunk)
                .ToArray();
        }

        var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 6)
        {
            return new[] { text.Trim() };
        }

        var chunks = new List<string>();
        for (var i = 0; i < words.Length; i += 4)
        {
            var slice = words.Skip(i).Take(4).ToArray();
            var chunk = string.Join(' ', slice);
            if (i + 4 < words.Length)
            {
                chunk += " ";
            }

            chunks.Add(chunk);
        }

        return chunks;
    }
}
