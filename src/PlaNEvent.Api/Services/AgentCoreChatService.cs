using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;
using Microsoft.Extensions.Options;
using PlaNEvent.Shared.Contracts;

namespace PlaNEvent.Api.Services;

public interface IAgentCoreChatService
{
    Task<AgentChatResponse> ChatAsync(AgentChatRequest request, CancellationToken cancellationToken);
}

public sealed class AgentCoreChatService(IAmazonBedrockAgentCore bedrockClient, IOptions<AgentCoreOptions> options) : IAgentCoreChatService
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
            var formatPass = await InvokeAndExtractAsync(htmlFormatPrompt, firstPass.SessionId, cancellationToken);

            var html = string.IsNullOrWhiteSpace(formatPass.Reply)
                ? $"<p>{WebUtility.HtmlEncode(answerText)}</p>"
                : formatPass.Reply;

            return new AgentChatResponse
            {
                SessionId = formatPass.SessionId,
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

    private async Task<(string SessionId, string Reply)> InvokeAndExtractAsync(string prompt, string sessionId, CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(new
        {
            prompt,
            message = prompt,
            inputText = prompt
        });

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
        var normalized = string.IsNullOrWhiteSpace(sessionId)
            ? $"session{Guid.NewGuid():N}"
            : sessionId.Trim();

        if (normalized.Length < 33)
        {
            normalized += Guid.NewGuid().ToString("N");
        }

        if (normalized.Length > 100)
        {
            normalized = normalized[..100];
        }

        return normalized;
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
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (TryGetString(root, "outputText", out var outputText)) return outputText;
            if (TryGetString(root, "response", out var response)) return response;
            if (TryGetString(root, "message", out var message)) return message;
            if (TryGetString(root, "completion", out var completion)) return completion;

            if (root.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.Array
                && content.GetArrayLength() > 0)
            {
                var first = content[0];
                if (TryGetString(first, "text", out var text)) return text;
                if (first.ValueKind == JsonValueKind.String) return first.GetString() ?? payload;
            }

            if (root.TryGetProperty("result", out var result))
            {
                if (TryGetString(result, "outputText", out var resultOutput)) return resultOutput;
                if (TryGetString(result, "response", out var resultResponse)) return resultResponse;
                if (TryGetString(result, "message", out var resultMessage)) return resultMessage;

                if (result.TryGetProperty("content", out var resultContent)
                    && resultContent.ValueKind == JsonValueKind.Array
                    && resultContent.GetArrayLength() > 0)
                {
                    foreach (var item in resultContent.EnumerateArray())
                    {
                        if (TryGetString(item, "text", out var resultText)) return resultText;
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            return item.GetString() ?? payload;
                        }
                    }
                }
            }
        }
        catch
        {
            // Fall through and return raw payload.
        }

        return payload;
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
}
