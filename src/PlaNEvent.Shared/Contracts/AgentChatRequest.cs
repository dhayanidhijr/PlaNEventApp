namespace PlaNEvent.Shared.Contracts;

public sealed class AgentChatRequest
{
    public string Message { get; set; } = string.Empty;
    public string? SessionId { get; set; }
}
