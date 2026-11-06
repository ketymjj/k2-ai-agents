using K2Agents.Api.Infrastructure;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Agents;
using K2Agents.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace K2Agents.Api.Controllers;

[ApiController]
[Route("api/v1/agents")]
[Authorize(Policy = Policies.Reader)]
public class AgentsController(IAgentService agents, IModelCatalog models) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AgentListItemDto>> List([FromQuery] AgentQuery query, CancellationToken ct) => agents.ListAsync(query, ct);

    [HttpGet("models")]
    public IReadOnlyList<AIModelOption> Models() => models.All;

    [HttpGet("templates")]
    public IReadOnlyList<AgentTemplateDto> Templates([FromQuery] string language = "pt-BR") => AgentTemplates.For(language);

    [HttpGet("{id:guid}")]
    public Task<AgentDto> Get(Guid id, CancellationToken ct) => agents.GetAsync(id, ct);

    [HttpGet("{id:guid}/versions")]
    public Task<IReadOnlyList<AgentVersionDto>> Versions(Guid id, CancellationToken ct) => agents.VersionsAsync(id, ct);

    [HttpPost("preview-prompt")]
    [Authorize(Policy = Policies.AgentManager)]
    public Task<PreviewPromptResponse> PreviewPrompt(PreviewPromptRequest request, CancellationToken ct) => agents.PreviewPromptAsync(request, ct);

    [HttpPost]
    [Authorize(Policy = Policies.AgentManager)]
    public async Task<ActionResult<AgentDto>> Create(SaveAgentRequest request, CancellationToken ct)
    {
        var created = await agents.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.AgentManager)]
    public Task<AgentDto> Update(Guid id, SaveAgentRequest request, CancellationToken ct) => agents.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/duplicate")]
    [Authorize(Policy = Policies.AgentManager)]
    public async Task<ActionResult<AgentDto>> Duplicate(Guid id, CancellationToken ct)
    {
        var copy = await agents.DuplicateAsync(id, ct);
        return CreatedAtAction(nameof(Get), new { id = copy.Id }, copy);
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = Policies.AgentManager)]
    public Task<AgentDto> SetStatus(Guid id, SetAgentStatusRequest request, CancellationToken ct) => agents.SetStatusAsync(id, request.Status, ct);

    [HttpPost("{id:guid}/versions/{version:int}/restore")]
    [Authorize(Policy = Policies.AgentManager)]
    public Task<AgentDto> Restore(Guid id, int version, CancellationToken ct) => agents.RestoreVersionAsync(id, version, ct);
}
