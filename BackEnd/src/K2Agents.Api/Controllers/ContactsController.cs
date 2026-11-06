using K2Agents.Api.Infrastructure;
using K2Agents.Application.Common;
using K2Agents.Application.Contacts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace K2Agents.Api.Controllers;

[ApiController]
[Route("api/v1/contacts")]
[Authorize(Policy = Policies.Reader)]
public class ContactsController(IContactService contacts) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ContactDto>> List([FromQuery] ContactQuery query, CancellationToken ct) => contacts.ListAsync(query, ct);

    [HttpGet("funnel")]
    public Task<FunnelDto> Funnel([FromQuery] Guid? companyId, CancellationToken ct) => contacts.FunnelAsync(companyId, ct);

    [HttpGet("{id:guid}")]
    public Task<ContactDetailDto> Get(Guid id, CancellationToken ct) => contacts.GetAsync(id, ct);

    [HttpPost]
    [Authorize(Policy = Policies.Attendant)]
    public async Task<ActionResult<ContactDto>> Create(SaveContactRequest request, CancellationToken ct)
    {
        var created = await contacts.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Attendant)]
    public Task<ContactDto> Update(Guid id, SaveContactRequest request, CancellationToken ct) => contacts.UpdateAsync(id, request, ct);

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = Policies.Attendant)]
    public Task<ContactDto> SetStatus(Guid id, SetContactStatusRequest request, CancellationToken ct) => contacts.SetStatusAsync(id, request.Status, ct);

    /// <summary>LGPD: remove dados pessoais do contato e das mensagens dele. Irreversível.</summary>
    [HttpPost("{id:guid}/anonymize")]
    [Authorize(Policy = Policies.CompanyAdmin)]
    public Task<ContactDto> Anonymize(Guid id, CancellationToken ct) => contacts.AnonymizeAsync(id, ct);
}
