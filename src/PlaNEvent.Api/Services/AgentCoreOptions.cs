namespace PlaNEvent.Api.Services;

public sealed class AgentCoreOptions
{
    public const string SectionName = "AgentCore";

    public string HostedAgentId { get; set; } = "hosted_agent_axa7j";
    public string AgentRuntimeArn { get; set; } = string.Empty;
    public string Qualifier { get; set; } = "DEFAULT";
    public string Region { get; set; } = "us-east-1";
    public string ApiBaseUrl { get; set; } = "https://planevent.dawindemoproductsdemo.com";
    public string SwaggerUrl { get; set; } = "https://planevent.dawindemoproductsdemo.com/swagger/v1/swagger.json";
    public bool ForwardUserToken { get; set; } = true;
}
