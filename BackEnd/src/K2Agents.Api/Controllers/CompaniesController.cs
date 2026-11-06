using K2Agents.Api.Infrastructure;
using K2Agents.Application.Common;
using K2Agents.Application.Companies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace K2Agents.Api.Controllers;

[ApiController]
[Route("api/v1/companies")]
[Authorize(Policy = Policies.Reader)]
public class CompaniesController(ICompanyService companies) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<CompanyDto>> List([FromQuery] CompanyQuery query, CancellationToken ct) => companies.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<CompanyDto> Get(Guid id, CancellationToken ct) => companies.GetAsync(id, ct);

    [HttpGet("{id:guid}/summary")]
    public Task<CompanySummaryDto> Summary(Guid id, CancellationToken ct) => companies.SummaryAsync(id, ct);

    [HttpPost]
    [Authorize(Policy = Policies.PlatformAdmin)]
    public async Task<ActionResult<CompanyDto>> Create(SaveCompanyRequest request, CancellationToken ct)
    {
        var created = await companies.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.CompanyAdmin)]
    public Task<CompanyDto> Update(Guid id, SaveCompanyRequest request, CancellationToken ct) => companies.UpdateAsync(id, request, ct);

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = Policies.PlatformAdmin)]
    public Task<CompanyDto> SetStatus(Guid id, SetCompanyStatusRequest request, CancellationToken ct)
        => companies.SetStatusAsync(id, request.Status, ct);
}
