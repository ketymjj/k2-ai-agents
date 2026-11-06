using K2Agents.Api.Infrastructure;
using K2Agents.Application.Playground;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace K2Agents.Api.Controllers;

/// <summary>Teste de agentes antes da publicação. Chamadas reais à IA somente no servidor.</summary>
[ApiController]
[Route("api/v1/playground")]
[Authorize(Policy = Policies.AgentManager)]
public class PlaygroundController(IPlaygroundService playground) : ControllerBase
{
    [HttpGet("status")]
    public Task<PlaygroundStatusDto> Status([FromQuery] Guid? agentId, CancellationToken ct) => playground.StatusAsync(agentId, ct);

    [HttpGet("sessions")]
    public Task<IReadOnlyList<PlaygroundSessionSummary>> List([FromQuery] Guid agentId, CancellationToken ct) => playground.ListAsync(agentId, ct);

    [HttpPost("sessions")]
    public async Task<ActionResult<PlaygroundSessionDto>> Start(StartSessionRequest request, CancellationToken ct)
    {
        var session = await playground.StartAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = session.Id }, session);
    }

    [HttpGet("sessions/{id:guid}")]
    public Task<PlaygroundSessionDto> Get(Guid id, CancellationToken ct) => playground.GetAsync(id, ct);

    [HttpPost("sessions/{id:guid}/messages")]
    [EnableRateLimiting(RateLimitPolicies.AI)]
    public Task<SendMessageResult> Send(Guid id, SendMessageRequest request, CancellationToken ct) => playground.SendAsync(id, request, ct);

    [HttpDelete("sessions/{id:guid}")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct)
    {
        await playground.CloseAsync(id, ct);
        return NoContent();
    }
}
