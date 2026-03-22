namespace PlaNEvent.Shared.Contracts;

public sealed class AgentChatResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string Reply { get; set; } = string.Empty;
    public string? HtmlReply { get; set; }
    public bool ActionCompleted { get; set; }
    public List<AgentChatActionDto> Actions { get; set; } = new();
    public AgentChatVerificationDto? Verification { get; set; }
}
