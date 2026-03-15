using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
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

            var htmlFormatPrompt = BuildHtmlFormatPrompt(answerText);
            var formatPass = await InvokeAndExtractAsync(htmlFormatPrompt, BuildFormattingSessionId(firstPass.SessionId), cancellationToken);

            var html = string.IsNullOrWhiteSpace(formatPass.Reply)
                ? $"<p>{WebUtility.HtmlEncode(answerText)}</p>"
                : formatPass.Reply;

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
            var result = await InvokeAndExtractAsync(request.Message, sessionId, cancellationToken);
            var reply = string.IsNullOrWhiteSpace(result.Reply) ? "No response from agent." : result.Reply;
            var htmlFormatPrompt = BuildHtmlFormatPrompt(reply);
            var formatPass = await InvokeAndExtractAsync(htmlFormatPrompt, BuildFormattingSessionId(result.SessionId), cancellationToken);
            var htmlReply = string.IsNullOrWhiteSpace(formatPass.Reply)
                ? $"<p>{WebUtility.HtmlEncode(reply)}</p>"
                : formatPass.Reply;

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

    private static string BuildHtmlFormatPrompt(string answerText)
    {
        return $"""
Format the following assistant answer as clean semantic HTML for a web chat response.
Rules:
- Return only valid HTML fragment (no markdown fences).
- Do not include <html>, <head>, <body>, <script>, or <style>.
- Use only safe tags like p, ul, ol, li, strong, em, code, pre, a, h1-h4, blockquote, br.
- Preserve meaning and structure.

Answer:
{answerText}
""";
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

    private static string BuildFormattingSessionId(string sessionId)
    {
        var baseId = string.IsNullOrWhiteSpace(sessionId)
            ? $"session{Guid.NewGuid():N}"
            : sessionId.Trim();

        var formattingSessionId = $"{baseId}-fmt";
        return formattingSessionId.Length > 100 ? formattingSessionId[..100] : formattingSessionId;
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
