namespace PlaNEvent.Shared.Contracts;

public sealed class AgentChatActionRequest
{
    public string? SessionId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string TemplateKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string SourceMessage { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "America/New_York";
}

public sealed class AgentChatActionDto
{
    public string ActionType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string TemplateKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Style { get; set; } = "secondary";
    public bool RequiresExecution { get; set; }
    public string NavigateUrl { get; set; } = string.Empty;
    public bool OpenInNewTab { get; set; }
    public AgentChatVerificationDto? Verification { get; set; }
}

public sealed class AgentChatVerificationDto
{
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string NavigateLabel { get; set; } = string.Empty;
    public string NavigateUrl { get; set; } = string.Empty;
    public List<AgentChatDetailDto> Details { get; set; } = new();
}

public sealed class AgentChatDetailDto
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
