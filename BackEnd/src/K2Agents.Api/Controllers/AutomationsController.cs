using K2Agents.Api.Infrastructure;
using K2Agents.Application.Automations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace K2Agents.Api.Controllers;

[ApiController]
[Route("api/v1/automations")]
[Authorize(Policy = Policies.Reader)]
public class AutomationsController(IAutomationService automations) : ControllerBase
{
    /// <summary>Gatilhos, ações permitidas por gatilho e placeholders dos textos.</summary>
    [HttpGet("catalog")]
    public AutomationCatalogDto Catalog() => automations.Catalog();

    [HttpGet]
    public Task<IReadOnlyList<AutomationRuleDto>> List([FromQuery] Guid? companyId, CancellationToken ct) => automations.ListAsync(companyId, ct);

    [HttpPost]
    [Authorize(Policy = Policies.CompanyAdmin)]
    public async Task<ActionResult<AutomationRuleDto>> Create(SaveAutomationRequest request, CancellationToken ct)
    {
        var created = await automations.CreateAsync(request, ct);
        return Created($"/api/v1/automations/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.CompanyAdmin)]
    public Task<AutomationRuleDto> Update(Guid id, SaveAutomationRequest request, CancellationToken ct) => automations.UpdateAsync(id, request, ct);

    [HttpPatch("{id:guid}/enabled")]
    [Authorize(Policy = Policies.CompanyAdmin)]
    public Task<AutomationRuleDto> SetEnabled(Guid id, SetAutomationEnabledRequest request, CancellationToken ct) =>
        automations.SetEnabledAsync(id, request.Enabled, ct);

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.CompanyAdmin)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await automations.DeleteAsync(id, ct);
        return NoContent();
    }
}
