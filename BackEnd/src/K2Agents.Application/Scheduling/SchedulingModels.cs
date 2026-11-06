using System.Text.Json;
using FluentValidation;
using K2Agents.Domain.Enums;

namespace K2Agents.Application.Scheduling;

public record ServiceOption(string Name, int DurationMinutes);

public record TimeRange(string Start, string End);

/// <summary>Regras da agenda da empresa. Horários em hora local do fuso configurado.</summary>
public record SchedulingSettings
{
    public string TimeZone { get; init; } = "America/Sao_Paulo";
    public int SlotMinutes { get; init; } = 30;
    public int MinAdvanceMinutes { get; init; } = 60;
    public int MaxDaysAhead { get; init; } = 30;
    public IReadOnlyList<ServiceOption> Services { get; init; } = [];
    /// <summary>Chave: dia da semana em inglês (monday...sunday). Dia ausente ou vazio = fechado.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<TimeRange>> WeeklyHours { get; init; } = new Dictionary<string, IReadOnlyList<TimeRange>>();

    public bool IsEnabled => Services.Count > 0 && WeeklyHours.Values.Any(r => r.Count > 0);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static SchedulingSettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new SchedulingSettings();
        try
        {
            return JsonSerializer.Deserialize<SchedulingSettings>(json, Json) ?? new SchedulingSettings();
        }
        catch (JsonException)
        {
            return new SchedulingSettings();
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public ServiceOption? FindService(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null
            : Services.FirstOrDefault(s => string.Equals(s.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
              ?? Services.FirstOrDefault(s => s.Name.Contains(name.Trim(), StringComparison.OrdinalIgnoreCase)
                                              || name.Contains(s.Name, StringComparison.OrdinalIgnoreCase));

    public static readonly string[] Days = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];
}

public record AppointmentDto(
    Guid Id, Guid CompanyId, string? CompanyName, Guid? ContactId, string? ContactName, string? ContactPhone, Guid? AgentId, string? AgentName,
    string ServiceName, DateTime StartDateTime, DateTime EndDateTime, AppointmentStatus Status, string? Notes, string Source,
    Guid? ConversationId, DateTime CreatedAt, DateTime UpdatedAt);

public record SlotDto(DateTime Start, DateTime End, string LocalLabel);

public record AvailabilityDto(string Date, string Service, int DurationMinutes, string TimeZone, IReadOnlyList<SlotDto> Slots);

public record SaveAppointmentRequest(
    Guid? CompanyId, Guid? ContactId, Guid? AgentId, string ServiceName, DateTime StartDateTime, int? DurationMinutes, string? Notes);

public record RescheduleRequest(DateTime StartDateTime, int? DurationMinutes);

public record SetAppointmentStatusRequest(AppointmentStatus Status);

public record AppointmentQuery
{
    public Guid? CompanyId { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public AppointmentStatus? Status { get; init; }
    public Guid? ContactId { get; init; }
    public Guid? AgentId { get; init; }
}

public class SchedulingSettingsValidator : AbstractValidator<SchedulingSettings>
{
    public SchedulingSettingsValidator()
    {
        RuleFor(x => x.TimeZone).Must(BeKnownTimeZone).WithMessage("Fuso horário inválido.");
        RuleFor(x => x.SlotMinutes).Must(m => m is 10 or 15 or 20 or 30 or 45 or 60).WithMessage("Intervalo deve ser 10, 15, 20, 30, 45 ou 60 minutos.");
        RuleFor(x => x.MinAdvanceMinutes).InclusiveBetween(0, 7 * 24 * 60);
        RuleFor(x => x.MaxDaysAhead).InclusiveBetween(1, 365);
        RuleFor(x => x.Services).Must(s => s.Count <= 50).WithMessage("Máximo de 50 serviços.")
            .Must(s => s.Select(x => x.Name.Trim().ToLowerInvariant()).Distinct().Count() == s.Count).WithMessage("Há serviços com nomes repetidos.");
        RuleForEach(x => x.Services).ChildRules(s =>
        {
            s.RuleFor(x => x.Name).NotEmpty().WithMessage("Informe o nome do serviço.").MaximumLength(120);
            s.RuleFor(x => x.DurationMinutes).InclusiveBetween(5, 8 * 60).WithMessage("Duração entre 5 minutos e 8 horas.");
        });
        RuleFor(x => x.WeeklyHours).Must(h => h.Keys.All(k => SchedulingSettings.Days.Contains(k))).WithMessage("Dia da semana inválido.")
            .Must(h => h.Values.All(ValidRanges)).WithMessage("Horários inválidos: use HH:mm, início antes do fim e sem sobreposição.");
    }

    private static bool BeKnownTimeZone(string id)
    {
        try { TimeZoneInfo.FindSystemTimeZoneById(id); return true; }
        catch { return false; }
    }

    private static bool ValidRanges(IReadOnlyList<TimeRange> ranges)
    {
        var parsed = new List<(TimeOnly Start, TimeOnly End)>();
        foreach (var r in ranges)
        {
            if (!TimeOnly.TryParseExact(r.Start, "HH:mm", out var s) || !TimeOnly.TryParseExact(r.End, "HH:mm", out var e) || s >= e) return false;
            parsed.Add((s, e));
        }
        var ordered = parsed.OrderBy(p => p.Start).ToList();
        for (var i = 1; i < ordered.Count; i++) if (ordered[i].Start < ordered[i - 1].End) return false;
        return true;
    }
}

public class SaveAppointmentRequestValidator : AbstractValidator<SaveAppointmentRequest>
{
    public SaveAppointmentRequestValidator()
    {
        RuleFor(x => x.ServiceName).NotEmpty().WithMessage("Informe o serviço.").MaximumLength(120);
        RuleFor(x => x.StartDateTime).NotEmpty();
        RuleFor(x => x.DurationMinutes).InclusiveBetween(5, 8 * 60).When(x => x.DurationMinutes.HasValue);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

/// <summary>Conversões e cálculo de horários livres. Funções puras — testáveis sem banco.</summary>
public static class Availability
{
    public static TimeZoneInfo Zone(SchedulingSettings s)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(s.TimeZone); }
        catch { return TimeZoneInfo.Utc; }
    }

    public static DateTime ToUtc(DateTime local, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    /// <summary>
    /// Horários de início livres em uma data local: dentro do expediente, respeitando antecedência mínima,
    /// prazo máximo e sem sobrepor os intervalos ocupados (UTC).
    /// </summary>
    public static IReadOnlyList<SlotDto> Slots(
        SchedulingSettings settings, DateOnly date, int durationMinutes, IReadOnlyList<(DateTime Start, DateTime End)> busyUtc, DateTime nowUtc)
    {
        var zone = Zone(settings);
        var day = SchedulingSettings.Days[((int)date.DayOfWeek + 6) % 7];
        if (!settings.WeeklyHours.TryGetValue(day, out var ranges) || ranges.Count == 0) return [];

        var earliest = nowUtc.AddMinutes(settings.MinAdvanceMinutes);
        var latest = nowUtc.Date.AddDays(settings.MaxDaysAhead + 1);
        var result = new List<SlotDto>();
        foreach (var range in ranges)
        {
            var open = date.ToDateTime(TimeOnly.ParseExact(range.Start, "HH:mm"));
            var close = date.ToDateTime(TimeOnly.ParseExact(range.End, "HH:mm"));
            for (var local = open; local.AddMinutes(durationMinutes) <= close; local = local.AddMinutes(settings.SlotMinutes))
            {
                var start = ToUtc(local, zone);
                var end = start.AddMinutes(durationMinutes);
                if (start < earliest || start >= latest) continue;
                if (busyUtc.Any(b => start < b.End && end > b.Start)) continue;
                result.Add(new SlotDto(start, end, local.ToString("dd/MM HH:mm")));
            }
        }
        return result;
    }

    /// <summary>O intervalo cabe inteiro em uma faixa de expediente do dia local?</summary>
    public static bool WithinBusinessHours(SchedulingSettings settings, DateTime startUtc, DateTime endUtc)
    {
        var zone = Zone(settings);
        var localStart = ToLocal(startUtc, zone);
        var localEnd = ToLocal(endUtc, zone);
        if (localStart.Date != localEnd.Date && localEnd.TimeOfDay != TimeSpan.Zero) return false;
        var day = SchedulingSettings.Days[((int)localStart.DayOfWeek + 6) % 7];
        if (!settings.WeeklyHours.TryGetValue(day, out var ranges)) return false;
        var s = TimeOnly.FromDateTime(localStart);
        var e = TimeOnly.FromDateTime(localEnd);
        return ranges.Any(r => s >= TimeOnly.ParseExact(r.Start, "HH:mm") && e <= TimeOnly.ParseExact(r.End, "HH:mm"));
    }
}
