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
            return new AgentChatResponse { SessionId = sessionId, Reply = "Please provide a message." };
        }

        if (string.IsNullOrWhiteSpace(optionsValue.AgentRuntimeArn))
        {
            return new AgentChatResponse
            {
                SessionId = sessionId,
                Reply = "AgentCore runtime ARN is not configured. Set AgentCore:AgentRuntimeArn in API settings."
            };
        }

        var payloadJson = JsonSerializer.Serialize(new
        {
            prompt = request.Message,
            message = request.Message,
            inputText = request.Message
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

        try
        {
            var response = await bedrockClient.InvokeAgentRuntimeAsync(invokeRequest, cancellationToken);
            var responseText = await ReadResponseAsync(response);

            return new AgentChatResponse
            {
                SessionId = response.RuntimeSessionId ?? sessionId,
                Reply = ExtractReply(responseText)
            };
        }
        catch (Exception ex)
        {
            return new AgentChatResponse
            {
                SessionId = sessionId,
                Reply = $"AgentCore call failed: {ex.Message}"
            };
        }
    }

    private static string NormalizeSessionId(string? sessionId)
    {
        var normalized = string.IsNullOrWhiteSpace(sessionId)
            ? $"session-{Guid.NewGuid():N}-{Guid.NewGuid():N}"
            : sessionId.Trim();

        if (normalized.Length < 33)
        {
            normalized = $"{normalized}-{Guid.NewGuid():N}";
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
