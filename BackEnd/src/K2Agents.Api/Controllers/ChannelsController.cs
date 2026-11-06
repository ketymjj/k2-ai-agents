using K2Agents.Api.Infrastructure;
using K2Agents.Application.Channels;
using K2Agents.Application.WhatsApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace K2Agents.Api.Controllers;

[ApiController]
[Route("api/v1/channels")]
[Authorize(Policy = Policies.Reader)]
public class ChannelsController(IChannelService channels) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ChannelDto>> List([FromQuery] Guid? companyId, CancellationToken ct) => channels.ListAsync(companyId, ct);

    [HttpGet("{id:guid}")]
    public Task<ChannelDto> Get(Guid id, CancellationToken ct) => channels.GetAsync(id, ct);

    [HttpPost("webchat")]
    [Authorize(Policy = Policies.AgentManager)]
    public async Task<ActionResult<ChannelDto>> CreateWebChat(SaveWebChatRequest request, CancellationToken ct)
    {
        var created = await channels.CreateWebChatAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}/webchat")]
    [Authorize(Policy = Policies.AgentManager)]
    public Task<ChannelDto> UpdateWebChat(Guid id, SaveWebChatRequest request, CancellationToken ct) => channels.UpdateWebChatAsync(id, request, ct);

    /// <summary>Credenciais da Meta são sensíveis: só administradores da empresa configuram o WhatsApp.</summary>
    [HttpPost("whatsapp")]
    [Authorize(Policy = Policies.CompanyAdmin)]
    public async Task<ActionResult<ChannelDto>> CreateWhatsApp(SaveWhatsAppRequest request, CancellationToken ct)
    {
        var created = await channels.CreateWhatsAppAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}/whatsapp")]
    [Authorize(Policy = Policies.CompanyAdmin)]
    public Task<ChannelDto> UpdateWhatsApp(Guid id, SaveWhatsAppRequest request, CancellationToken ct) => channels.UpdateWhatsAppAsync(id, request, ct);

    /// <summary>Chamada real à Graph API da Meta com as credenciais salvas.</summary>
    [HttpPost("{id:guid}/whatsapp/test")]
    [Authorize(Policy = Policies.CompanyAdmin)]
    [EnableRateLimiting(RateLimitPolicies.AI)]
    public Task<WhatsAppConnectionResult> TestWhatsApp(Guid id, CancellationToken ct) => channels.TestWhatsAppAsync(id, ct);

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = Policies.AgentManager)]
    public Task<ChannelDto> SetStatus(Guid id, SetChannelStatusRequest request, CancellationToken ct) => channels.SetStatusAsync(id, request.Status, ct);

    [HttpPost("{id:guid}/regenerate-token")]
    [Authorize(Policy = Policies.AgentManager)]
    public Task<ChannelDto> RegenerateToken(Guid id, CancellationToken ct) => channels.RegenerateTokenAsync(id, ct);

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.AgentManager)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await channels.DeleteAsync(id, ct);
        return NoContent();
    }
}
