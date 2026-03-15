using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaNEvent.Api.Services;
using PlaNEvent.Shared.Contracts;

namespace PlaNEvent.Api.Controllers;

[ApiController]
[Route("api/agent/chat")]
[Authorize]
public sealed class AgentChatController(IAgentCoreChatService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<AgentChatResponse>> Chat(AgentChatRequest request, CancellationToken cancellationToken)
    {
        var response = await service.ChatAsync(request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("stream")]
    [Produces("text/event-stream")]
    public async Task Stream(AgentChatRequest request, CancellationToken cancellationToken)
    {
        await service.StreamChatAsync(request, Response, cancellationToken);
    }
}
