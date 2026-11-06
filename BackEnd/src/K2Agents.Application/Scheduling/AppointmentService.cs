using FluentValidation;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Automations;
using K2Agents.Application.Common;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Application.Scheduling;

/// <summary>Reserva feita pelo agente de IA (após consultar disponibilidade).</summary>
public record AgentBookingRequest(Guid CompanyId, Guid AgentId, Guid? ConversationId, Guid? ContactId, string ServiceName, DateTime StartUtc);

public interface IAppointmentService
{
    Task<SchedulingSettings> GetSettingsAsync(Guid? companyId, CancellationToken ct);
    Task<SchedulingSettings> SaveSettingsAsync(Guid? companyId, SchedulingSettings settings, CancellationToken ct);
    Task<IReadOnlyList<AppointmentDto>> ListAsync(AppointmentQuery query, CancellationToken ct);
    Task<AppointmentDto> GetAsync(Guid id, CancellationToken ct);
    Task<AvailabilityDto> AvailabilityAsync(Guid? companyId, DateOnly date, string service, Guid? excludeAppointmentId, CancellationToken ct);
    /// <summary>Disponibilidade escopada pela empresa do agente (chamadas sem usuário logado, ex.: Web Chat).</summary>
    Task<AvailabilityDto> CompanyAvailabilityAsync(Guid companyId, DateOnly date, string service, CancellationToken ct);
    Task<AppointmentDto> CreateAsync(SaveAppointmentRequest request, CancellationToken ct);
    Task<AppointmentDto> RescheduleAsync(Guid id, RescheduleRequest request, CancellationToken ct);
    Task<AppointmentDto> SetStatusAsync(Guid id, AppointmentStatus status, CancellationToken ct);
    /// <summary>Usado pelas ferramentas do agente: só aceita horários livres dentro das regras da agenda.</summary>
    Task<AppointmentDto> BookForAgentAsync(AgentBookingRequest request, CancellationToken ct);
    /// <summary>Próximos horários livres a partir de hoje (usado pelo agente e pelo modo demonstrativo).</summary>
    Task<IReadOnlyList<SlotDto>> NextSlotsAsync(Guid companyId, ServiceOption service, int max, CancellationToken ct);
}

/// <summary>
/// Agenda da empresa. Regra central: dois agendamentos ativos (agendado/confirmado) nunca se sobrepõem.
/// A verificação e a gravação acontecem sob trava exclusiva da agenda da empresa.
/// </summary>
public class AppointmentService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IAuditService audit,
    IAutomationEngine automations,
    IValidator<SchedulingSettings> settingsValidator,
    IValidator<SaveAppointmentRequest> saveValidator) : IAppointmentService
{
    private static readonly AppointmentStatus[] Active = [AppointmentStatus.Scheduled, AppointmentStatus.Confirmed];

    public async Task<SchedulingSettings> GetSettingsAsync(Guid? companyId, CancellationToken ct)
    {
        var id = ResolveCompany(companyId);
        var json = await db.Companies.Where(c => c.Id == id).Select(c => c.SchedulingJson).FirstOrDefaultAsync(ct)
                   ?? throw AppException.NotFound("Empresa");
        return SchedulingSettings.Parse(json);
    }

    public async Task<SchedulingSettings> SaveSettingsAsync(Guid? companyId, SchedulingSettings settings, CancellationToken ct)
    {
        await settingsValidator.ValidateAndThrowAsync(settings, ct);
        var id = ResolveCompany(companyId);
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw AppException.NotFound("Empresa");
        var normalized = settings with
        {
            Services = settings.Services.Select(s => s with { Name = s.Name.Trim() }).ToList(),
            WeeklyHours = settings.WeeklyHours.ToDictionary(k => k.Key, v => (IReadOnlyList<TimeRange>)v.Value.OrderBy(r => r.Start).ToList())
        };
        company.SchedulingJson = normalized.Serialize();
        audit.Record("scheduling.settings_updated", nameof(Company), id.ToString(), $"{normalized.Services.Count} serviços", id);
        await db.SaveChangesAsync(ct);
        return normalized;
    }

    public async Task<IReadOnlyList<AppointmentDto>> ListAsync(AppointmentQuery query, CancellationToken ct)
    {
        var from = query.From ?? DateTime.UtcNow.Date.AddDays(-7);
        var to = query.To ?? from.AddDays(45);
        var appointments = db.Appointments.AsNoTracking().Where(a => a.StartDateTime < to && a.EndDateTime > from);
        if (query.CompanyId.HasValue) appointments = appointments.Where(a => a.CompanyId == query.CompanyId);
        if (query.Status.HasValue) appointments = appointments.Where(a => a.Status == query.Status);
        if (query.ContactId.HasValue) appointments = appointments.Where(a => a.ContactId == query.ContactId);
        if (query.AgentId.HasValue) appointments = appointments.Where(a => a.AgentId == query.AgentId);
        return await Project(appointments.OrderBy(a => a.StartDateTime).Take(500)).ToListAsync(ct);
    }

    public async Task<AppointmentDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project(db.Appointments.AsNoTracking().Where(a => a.Id == id)).FirstOrDefaultAsync(ct) ?? throw AppException.NotFound("Agendamento");

    public Task<AvailabilityDto> AvailabilityAsync(Guid? companyId, DateOnly date, string service, Guid? excludeAppointmentId, CancellationToken ct) =>
        AvailabilityCoreAsync(ResolveCompany(companyId), date, service, excludeAppointmentId, ct);

    public Task<AvailabilityDto> CompanyAvailabilityAsync(Guid companyId, DateOnly date, string service, CancellationToken ct) =>
        AvailabilityCoreAsync(companyId, date, service, null, ct);

    private async Task<AvailabilityDto> AvailabilityCoreAsync(Guid id, DateOnly date, string service, Guid? excludeAppointmentId, CancellationToken ct)
    {
        var settings = await SettingsOfAsync(id, ct);
        var option = settings.FindService(service) ?? throw new AppException("Serviço não encontrado na agenda.", 404, "service_not_found");
        var slots = await SlotsAsync(id, settings, date, option.DurationMinutes, excludeAppointmentId, ct);
        return new AvailabilityDto(date.ToString("yyyy-MM-dd"), option.Name, option.DurationMinutes, settings.TimeZone, slots);
    }

    public async Task<IReadOnlyList<SlotDto>> NextSlotsAsync(Guid companyId, ServiceOption service, int max, CancellationToken ct)
    {
        var settings = await SettingsOfAsync(companyId, ct);
        var zone = Availability.Zone(settings);
        var today = DateOnly.FromDateTime(Availability.ToLocal(DateTime.UtcNow, zone));
        var result = new List<SlotDto>();
        for (var d = 0; d <= settings.MaxDaysAhead && result.Count < max; d++)
        {
            var daySlots = await SlotsAsync(companyId, settings, today.AddDays(d), service.DurationMinutes, null, ct);
            // Espaça as sugestões no mesmo dia para oferecer opções variadas.
            result.AddRange(daySlots.Where((_, i) => i % 2 == 0).Take(max - result.Count));
        }
        return result;
    }

    public async Task<AppointmentDto> CreateAsync(SaveAppointmentRequest request, CancellationToken ct)
    {
        await saveValidator.ValidateAndThrowAsync(request, ct);
        var companyId = await ResolveCompanyForContactAsync(request.CompanyId, request.ContactId, ct);
        var settings = await SettingsOfAsync(companyId, ct);
        var service = settings.FindService(request.ServiceName);
        var duration = request.DurationMinutes ?? service?.DurationMinutes
                       ?? throw new AppException("Informe a duração ou escolha um serviço cadastrado.", 400, "duration_required");
        await EnsureAgentAsync(companyId, request.AgentId, ct);

        var start = AsUtc(request.StartDateTime);
        var appointment = new Appointment
        {
            CompanyId = companyId,
            ContactId = request.ContactId,
            AgentId = request.AgentId,
            ServiceName = service?.Name ?? request.ServiceName.Trim(),
            StartDateTime = start,
            EndDateTime = start.AddMinutes(duration),
            Notes = ValidationRules.TrimToNull(request.Notes),
            Source = "Manual"
        };
        // A equipe pode marcar fora do expediente (exceções), mas nunca sobrepor outro horário.
        await SaveExclusiveAsync(appointment, null, ct);
        audit.Record("appointment.created", nameof(Appointment), appointment.Id.ToString(), appointment.ServiceName, companyId);
        await db.SaveChangesAsync(ct);
        await PublishCreatedAsync(appointment, ct);
        return await GetAsync(appointment.Id, ct);
    }

    public async Task<AppointmentDto> BookForAgentAsync(AgentBookingRequest request, CancellationToken ct)
    {
        var settings = await SettingsOfAsync(request.CompanyId, ct);
        var service = settings.FindService(request.ServiceName) ?? throw new AppException("Serviço não encontrado na agenda.", 404, "service_not_found");
        var start = AsUtc(request.StartUtc);

        var appointment = new Appointment
        {
            CompanyId = request.CompanyId,
            AgentId = request.AgentId,
            ContactId = request.ContactId,
            ConversationId = request.ConversationId,
            ServiceName = service.Name,
            StartDateTime = start,
            EndDateTime = start.AddMinutes(service.DurationMinutes),
            Source = "Agent",
            Notes = "Agendado pelo assistente virtual após consulta de disponibilidade."
        };
        await SaveExclusiveAsync(appointment, settings, ct);
        audit.Record("appointment.booked_by_agent", nameof(Appointment), appointment.Id.ToString(), service.Name, request.CompanyId);
        await db.SaveChangesAsync(ct);
        await PublishCreatedAsync(appointment, ct);
        return await Project(db.Appointments.AsNoTracking().IgnoreQueryFilters().Where(a => a.Id == appointment.Id)).FirstAsync(ct);
    }

    public async Task<AppointmentDto> RescheduleAsync(Guid id, RescheduleRequest request, CancellationToken ct)
    {
        var appointment = await LoadAsync(id, ct);
        if (!Active.Contains(appointment.Status))
            throw new AppException("Só é possível reagendar agendamentos ativos.", 409, "invalid_transition");

        var duration = request.DurationMinutes ?? (int)(appointment.EndDateTime - appointment.StartDateTime).TotalMinutes;
        if (duration is < 5 or > 480) throw new AppException("Duração inválida.", 400, "invalid_duration");
        var oldStart = appointment.StartDateTime;
        appointment.StartDateTime = AsUtc(request.StartDateTime);
        appointment.EndDateTime = appointment.StartDateTime.AddMinutes(duration);
        appointment.Status = AppointmentStatus.Scheduled;
        appointment.UpdatedAt = DateTime.UtcNow;

        await SaveExclusiveAsync(appointment, null, ct, isNew: false);
        audit.Record("appointment.rescheduled", nameof(Appointment), id.ToString(), $"{oldStart:O} -> {appointment.StartDateTime:O}", appointment.CompanyId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<AppointmentDto> SetStatusAsync(Guid id, AppointmentStatus status, CancellationToken ct)
    {
        if (!Enum.IsDefined(status)) throw new AppException("Status inválido.", 400, "invalid_status");
        var appointment = await LoadAsync(id, ct);
        if (appointment.Status == status) return await GetAsync(id, ct);

        var allowed = appointment.Status switch
        {
            AppointmentStatus.Scheduled => status is AppointmentStatus.Confirmed or AppointmentStatus.Cancelled or AppointmentStatus.Completed or AppointmentStatus.NoShow,
            AppointmentStatus.Confirmed => status is AppointmentStatus.Cancelled or AppointmentStatus.Completed or AppointmentStatus.NoShow,
            _ => false // cancelado/concluído/não compareceu são finais
        };
        if (!allowed) throw new AppException("Transição de status não permitida.", 409, "invalid_transition");

        audit.Record("appointment.status_changed", nameof(Appointment), id.ToString(), $"{appointment.Status} -> {status}", appointment.CompanyId);
        appointment.Status = status;
        appointment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ---------- Núcleo da regra de conflito ----------

    /// <summary>
    /// Verifica conflito e grava sob trava exclusiva da agenda. Com <paramref name="rules"/>, aplica também as regras
    /// de disponibilidade (expediente, antecedência, prazo) — usado para reservas feitas pelo agente.
    /// </summary>
    private Task SaveExclusiveAsync(Appointment appointment, SchedulingSettings? rules, CancellationToken ct, bool isNew = true) =>
        db.RunScheduleExclusiveAsync(appointment.CompanyId, token => CheckAndSaveAsync(appointment, rules, isNew, token), ct);

    private async Task CheckAndSaveAsync(Appointment appointment, SchedulingSettings? rules, bool isNew, CancellationToken ct)
    {
        var conflict = await db.Appointments.IgnoreQueryFilters().AnyAsync(a =>
            a.CompanyId == appointment.CompanyId && a.Id != appointment.Id && Active.Contains(a.Status)
            && a.StartDateTime < appointment.EndDateTime && a.EndDateTime > appointment.StartDateTime, ct);
        if (conflict) throw new AppException("Este horário já está ocupado. Escolha outro horário.", 409, "slot_unavailable");

        if (rules is not null)
        {
            var now = DateTime.UtcNow;
            if (appointment.StartDateTime < now.AddMinutes(rules.MinAdvanceMinutes))
                throw new AppException($"Agendamentos precisam de pelo menos {rules.MinAdvanceMinutes} minutos de antecedência.", 409, "too_soon");
            if (appointment.StartDateTime > now.AddDays(rules.MaxDaysAhead + 1))
                throw new AppException($"A agenda está aberta para os próximos {rules.MaxDaysAhead} dias.", 409, "too_far");
            if (!Availability.WithinBusinessHours(rules, appointment.StartDateTime, appointment.EndDateTime))
                throw new AppException("Horário fora do expediente.", 409, "outside_business_hours");
        }

        if (isNew) db.Appointments.Add(appointment); // numa nova tentativa, a entidade já rastreada continua Added
        await db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<SlotDto>> SlotsAsync(
        Guid companyId, SchedulingSettings settings, DateOnly date, int duration, Guid? excludeId, CancellationToken ct)
    {
        var zone = Availability.Zone(settings);
        var dayStart = Availability.ToUtc(date.ToDateTime(TimeOnly.MinValue), zone);
        var dayEnd = dayStart.AddDays(1);
        var busy = await db.Appointments.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.CompanyId == companyId && Active.Contains(a.Status) && a.Id != excludeId
                        && a.StartDateTime < dayEnd && a.EndDateTime > dayStart)
            .Select(a => new { a.StartDateTime, a.EndDateTime })
            .ToListAsync(ct);
        return Availability.Slots(settings, date, duration, busy.Select(b => (b.StartDateTime, b.EndDateTime)).ToList(), DateTime.UtcNow);
    }

    private async Task PublishCreatedAsync(Appointment a, CancellationToken ct)
    {
        var settings = await SettingsOfAsync(a.CompanyId, ct);
        var local = Availability.ToLocal(a.StartDateTime, Availability.Zone(settings));
        var contact = a.ContactId is null ? null
            : await db.Contacts.IgnoreQueryFilters().Where(c => c.Id == a.ContactId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var agent = a.AgentId is null ? null
            : await db.AIAgents.IgnoreQueryFilters().Where(x => x.Id == a.AgentId).Select(x => x.Name).FirstOrDefaultAsync(ct);
        await automations.PublishAsync(new AutomationEvent(a.CompanyId, AutomationTrigger.AppointmentCreated,
            new Dictionary<string, string>
            {
                ["servico"] = a.ServiceName,
                ["data"] = local.ToString("dd/MM/yyyy HH:mm"),
                ["contato"] = contact ?? "Cliente não identificado",
                ["agente"] = agent ?? "Equipe",
                ["canal"] = a.Source == "Agent" ? "assistente virtual" : "equipe"
            }, $"/appointments?id={a.Id}"), ct);
    }

    private async Task<SchedulingSettings> SettingsOfAsync(Guid companyId, CancellationToken ct) =>
        SchedulingSettings.Parse(await db.Companies.IgnoreQueryFilters().Where(c => c.Id == companyId).Select(c => c.SchedulingJson).FirstOrDefaultAsync(ct));

    private async Task<Appointment> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Appointments.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw AppException.NotFound("Agendamento");

    private Guid ResolveCompany(Guid? requested)
    {
        if (currentUser.IsSuperAdmin) return requested ?? throw new AppException("Informe a empresa.", 400, "company_required");
        if (requested.HasValue && requested != currentUser.CompanyId) throw AppException.Forbidden();
        return currentUser.CompanyId ?? throw AppException.Forbidden();
    }

    private async Task<Guid> ResolveCompanyForContactAsync(Guid? requested, Guid? contactId, CancellationToken ct)
    {
        Guid? contactCompany = null;
        if (contactId.HasValue)
            contactCompany = await db.Contacts.Where(c => c.Id == contactId).Select(c => (Guid?)c.CompanyId).FirstOrDefaultAsync(ct)
                             ?? throw AppException.NotFound("Contato");
        var companyId = currentUser.IsSuperAdmin ? requested ?? contactCompany ?? throw new AppException("Informe a empresa.", 400, "company_required")
            : ResolveCompany(requested);
        if (contactCompany.HasValue && contactCompany != companyId) throw new AppException("O contato pertence a outra empresa.", 400, "contact_company_mismatch");
        return companyId;
    }

    private async Task EnsureAgentAsync(Guid companyId, Guid? agentId, CancellationToken ct)
    {
        if (agentId is null) return;
        var agentCompany = await db.AIAgents.Where(a => a.Id == agentId).Select(a => (Guid?)a.CompanyId).FirstOrDefaultAsync(ct)
                           ?? throw AppException.NotFound("Agente");
        if (agentCompany != companyId) throw new AppException("O agente pertence a outra empresa.", 400, "agent_company_mismatch");
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private IQueryable<AppointmentDto> Project(IQueryable<Appointment> query) =>
        query.Select(a => new AppointmentDto(
            a.Id, a.CompanyId,
            db.Companies.Where(c => c.Id == a.CompanyId).Select(c => c.Name).FirstOrDefault(),
            a.ContactId, a.Contact != null ? a.Contact.Name : null, a.Contact != null ? a.Contact.Phone : null,
            a.AgentId, a.Agent != null ? a.Agent.Name : null, a.ServiceName,
            DateTime.SpecifyKind(a.StartDateTime, DateTimeKind.Utc), DateTime.SpecifyKind(a.EndDateTime, DateTimeKind.Utc),
            a.Status, a.Notes, a.Source, a.ConversationId, a.CreatedAt, a.UpdatedAt));
}
