namespace PlaNEvent.Shared.Contracts;

public sealed class AgentChatResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string Reply { get; set; } = string.Empty;
}
