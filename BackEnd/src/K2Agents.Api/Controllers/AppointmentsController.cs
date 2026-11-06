using K2Agents.Api.Infrastructure;
using K2Agents.Application.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace K2Agents.Api.Controllers;

[ApiController]
[Route("api/v1/appointments")]
[Authorize(Policy = Policies.Reader)]
public class AppointmentsController(IAppointmentService appointments) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<AppointmentDto>> List([FromQuery] AppointmentQuery query, CancellationToken ct) => appointments.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<AppointmentDto> Get(Guid id, CancellationToken ct) => appointments.GetAsync(id, ct);

    /// <summary>Horários livres reais de um serviço em uma data (local da empresa).</summary>
    [HttpGet("availability")]
    public Task<AvailabilityDto> Availability([FromQuery] DateOnly date, [FromQuery] string service, [FromQuery] Guid? companyId,
        [FromQuery] Guid? excludeId, CancellationToken ct) => appointments.AvailabilityAsync(companyId, date, service, excludeId, ct);

    [HttpGet("settings")]
    public Task<SchedulingSettings> GetSettings([FromQuery] Guid? companyId, CancellationToken ct) => appointments.GetSettingsAsync(companyId, ct);

    [HttpPut("settings")]
    [Authorize(Policy = Policies.AgentManager)]
    public Task<SchedulingSettings> SaveSettings([FromQuery] Guid? companyId, SchedulingSettings settings, CancellationToken ct) =>
        appointments.SaveSettingsAsync(companyId, settings, ct);

    [HttpPost]
    [Authorize(Policy = Policies.Attendant)]
    public async Task<ActionResult<AppointmentDto>> Create(SaveAppointmentRequest request, CancellationToken ct)
    {
        var created = await appointments.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/reschedule")]
    [Authorize(Policy = Policies.Attendant)]
    public Task<AppointmentDto> Reschedule(Guid id, RescheduleRequest request, CancellationToken ct) => appointments.RescheduleAsync(id, request, ct);

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = Policies.Attendant)]
    public Task<AppointmentDto> SetStatus(Guid id, SetAppointmentStatusRequest request, CancellationToken ct) =>
        appointments.SetStatusAsync(id, request.Status, ct);
}
