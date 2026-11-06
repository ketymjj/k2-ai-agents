using System.Globalization;
using System.Text;
using System.Text.Json;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Common;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Application.Scheduling;

/// <param name="CustomerTexts">Mensagens escritas pelo cliente nesta conversa (inclui a atual) — base da checagem anti-invenção.</param>
public record AgentToolContext(AIAgent Agent, Guid? ConversationId, ChannelType Channel, IReadOnlyList<string> CustomerTexts)
{
    public bool IsPlayground => Channel == ChannelType.Playground;
}

public interface IAgentTools
{
    /// <summary>Ferramentas de agenda: agentes do tipo Agendamento em empresas com agenda configurada.</summary>
    Task<SchedulingSettings?> SettingsIfEnabledAsync(AIAgent agent, CancellationToken ct);
    IReadOnlyList<AIToolDefinition> Definitions();
    string Instructions(SchedulingSettings settings, string language);
    /// <summary>Executa e devolve JSON para o modelo. Nunca lança: erros viram {"ok":false,"error":...}.</summary>
    Task<string> ExecuteAsync(AIToolCall call, AgentToolContext ctx, CancellationToken ct);
    Task<IReadOnlyList<SlotDto>> NextSlotsAsync(Guid companyId, ServiceOption service, int max, CancellationToken ct);
    /// <summary>Reserva já validada; usada pela ferramenta e pelo modo demonstrativo.</summary>
    Task<(bool Ok, string Message, AppointmentDto? Appointment)> BookAsync(AgentToolContext ctx, string service, DateTime localStart, string? name, string? phone, CancellationToken ct);
}

/// <summary>
/// Ferramentas que ligam o agente à agenda real. O modelo nunca grava nada diretamente: ele pede, o servidor valida
/// (serviço, expediente, antecedência, conflito, dados do cliente) e responde com o resultado.
/// </summary>
public class AgentTools(IAppDbContext db, IAppointmentService appointments, IAuditService audit) : IAgentTools
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SchedulingSettings?> SettingsIfEnabledAsync(AIAgent agent, CancellationToken ct)
    {
        if (agent.BusinessType != BusinessType.Scheduling) return null;
        var json = await db.Companies.IgnoreQueryFilters().Where(c => c.Id == agent.CompanyId).Select(c => c.SchedulingJson).FirstOrDefaultAsync(ct);
        var settings = SchedulingSettings.Parse(json);
        return settings.IsEnabled ? settings : null;
    }

    public Task<IReadOnlyList<SlotDto>> NextSlotsAsync(Guid companyId, ServiceOption service, int max, CancellationToken ct) =>
        appointments.NextSlotsAsync(companyId, service, max, ct);

    public IReadOnlyList<AIToolDefinition> Definitions() =>
    [
        new("list_services", "Lista os serviços agendáveis com a duração de cada um.",
            new { type = "object", properties = new { }, additionalProperties = false }),
        new("check_availability", "Consulta os horários livres reais de um serviço em uma data. Use SEMPRE antes de oferecer horários.",
            new
            {
                type = "object",
                properties = new
                {
                    service = new { type = "string", description = "Nome do serviço, como retornado por list_services." },
                    date = new { type = "string", description = "Data no formato YYYY-MM-DD (horário local da empresa)." }
                },
                required = new[] { "service", "date" },
                additionalProperties = false
            }),
        new("book_appointment", "Registra o agendamento em um horário livre. Só chame depois que o cliente escolher o horário e informar o nome.",
            new
            {
                type = "object",
                properties = new
                {
                    service = new { type = "string" },
                    date = new { type = "string", description = "YYYY-MM-DD" },
                    time = new { type = "string", description = "HH:mm (horário local)" },
                    customer_name = new { type = "string", description = "Nome exatamente como o cliente escreveu." },
                    customer_phone = new { type = "string", description = "Telefone exatamente como o cliente escreveu (opcional)." }
                },
                required = new[] { "service", "date", "time" },
                additionalProperties = false
            })
    ];

    public string Instructions(SchedulingSettings settings, string language)
    {
        var local = Availability.ToLocal(DateTime.UtcNow, Availability.Zone(settings));
        var culture = CultureInfo.GetCultureInfo(language == "en" ? "en-US" : "pt-BR");
        var today = local.ToString("dddd, yyyy-MM-dd HH:mm", culture);
        return language == "en"
            ? $"""
              ## Real schedule (tools)
              - Today is {today} ({settings.TimeZone}). Resolve relative dates ("tomorrow", "Friday") from this date.
              - Use check_availability before offering any time. Only offer times returned by the tool.
              - Before booking, ask for the customer's name. Never invent names or phone numbers.
              - Only say the appointment is booked after book_appointment returns "ok": true. If it fails, explain and offer other times.
              """
            : $"""
              ## Agenda real (ferramentas)
              - Hoje é {today} ({settings.TimeZone}). Converta datas relativas ("amanhã", "sexta") a partir desta data.
              - Use check_availability antes de oferecer qualquer horário. Ofereça apenas horários retornados pela ferramenta.
              - Antes de agendar, peça o nome do cliente. Nunca invente nomes ou telefones.
              - Só diga que o horário está agendado depois que book_appointment retornar "ok": true. Se falhar, explique e ofereça outros horários.
              """;
    }

    public async Task<string> ExecuteAsync(AIToolCall call, AgentToolContext ctx, CancellationToken ct)
    {
        try
        {
            var settings = await SettingsIfEnabledAsync(ctx.Agent, ct);
            if (settings is null) return Error("A agenda não está disponível para este agente.");
            using var args = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments);
            var root = args.RootElement;

            switch (call.Name)
            {
                case "list_services":
                    return Ok(new
                    {
                        time_zone = settings.TimeZone,
                        services = settings.Services.Select(s => new { name = s.Name, duration_minutes = s.DurationMinutes })
                    });

                case "check_availability":
                {
                    var service = settings.FindService(Str(root, "service"));
                    if (service is null) return Error("Serviço não encontrado. Use list_services.");
                    if (!DateOnly.TryParseExact(Str(root, "date"), "yyyy-MM-dd", out var date)) return Error("Data inválida. Use YYYY-MM-DD.");
                    var availability = await appointments.CompanyAvailabilityAsync(ctx.Agent.CompanyId, date, service.Name, ct);
                    var zone = Availability.Zone(settings);
                    var times = availability.Slots.Select(s => Availability.ToLocal(s.Start, zone).ToString("HH:mm")).ToList();
                    object? alternatives = null;
                    if (times.Count == 0)
                        alternatives = (await appointments.NextSlotsAsync(ctx.Agent.CompanyId, service, 3, ct))
                            .Select(s => Availability.ToLocal(s.Start, zone).ToString("yyyy-MM-dd HH:mm"));
                    return Ok(new { service = service.Name, duration_minutes = service.DurationMinutes, date = Str(root, "date"), available_times = times, next_available = alternatives });
                }

                case "book_appointment":
                {
                    if (!DateTime.TryParseExact($"{Str(root, "date")} {Str(root, "time")}", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
                        return Error("Data ou hora inválida. Use YYYY-MM-DD e HH:mm.");
                    var (ok, message, appointment) = await BookAsync(ctx, Str(root, "service") ?? string.Empty, local, Str(root, "customer_name"), Str(root, "customer_phone"), ct);
                    return ok
                        ? Ok(new { message, appointment = appointment is null ? null : new { service = appointment.ServiceName, date = local.ToString("yyyy-MM-dd"), time = local.ToString("HH:mm"), status = appointment.Status.ToString() }, dry_run = ctx.IsPlayground })
                        : Error(message);
                }

                default:
                    return Error($"Ferramenta desconhecida: {call.Name}.");
            }
        }
        catch (JsonException)
        {
            return Error("Argumentos inválidos.");
        }
        catch (AppException ex)
        {
            return Error(ex.Message);
        }
    }

    public async Task<(bool Ok, string Message, AppointmentDto? Appointment)> BookAsync(
        AgentToolContext ctx, string serviceName, DateTime localStart, string? name, string? phone, CancellationToken ct)
    {
        var settings = await SettingsIfEnabledAsync(ctx.Agent, ct);
        if (settings is null) return (false, "A agenda não está disponível para este agente.", null);
        var service = settings.FindService(serviceName);
        if (service is null) return (false, "Serviço não encontrado.", null);

        var conversation = ctx.ConversationId is null ? null
            : await db.Conversations.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == ctx.ConversationId, ct);
        Guid? contactId = conversation?.ContactId;

        // Anti-invenção: dados pessoais só são aceitos se o próprio cliente os escreveu na conversa.
        var customerText = Normalize(string.Join(' ', ctx.CustomerTexts));
        var customerDigits = new string(customerText.Where(char.IsDigit).ToArray());
        if (!string.IsNullOrWhiteSpace(name) && !customerText.Contains(Normalize(name)))
            return (false, "O nome informado não aparece nas mensagens do cliente. Peça o nome ao cliente.", null);
        var phoneDigits = ValidationRules.DigitsOnly(phone);
        if (phoneDigits is { Length: > 0 } && !customerDigits.Contains(phoneDigits))
            return (false, "O telefone informado não aparece nas mensagens do cliente. Peça o telefone ao cliente.", null);
        if (contactId is null && string.IsNullOrWhiteSpace(name))
            return (false, "Pergunte o nome do cliente antes de agendar.", null);

        var startUtc = Availability.ToUtc(localStart, Availability.Zone(settings));

        if (ctx.IsPlayground)
        {
            // Ensaio: valida o horário contra a agenda real, mas não grava nada.
            var free = (await appointments.CompanyAvailabilityAsync(ctx.Agent.CompanyId, DateOnly.FromDateTime(localStart), service.Name, ct))
                .Slots.Any(s => s.Start == startUtc);
            return free
                ? (true, "Teste do Playground: o horário está livre, mas nenhum agendamento foi gravado.", null)
                : (false, "Horário indisponível.", null);
        }

        if (contactId is null)
        {
            var contact = phoneDigits is null ? null
                : await db.Contacts.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.CompanyId == ctx.Agent.CompanyId && c.Phone == phoneDigits && !c.IsAnonymized, ct);
            if (contact is null)
            {
                contact = new Contact
                {
                    CompanyId = ctx.Agent.CompanyId, Name = name!.Trim(), Phone = phoneDigits, Source = ctx.Channel.ToString(),
                    Status = ContactStatus.Qualified, IsLead = true
                };
                db.Contacts.Add(contact);
                audit.Record("contact.captured", nameof(Contact), contact.Id.ToString(), "agendamento pelo assistente", ctx.Agent.CompanyId);
            }
            contactId = contact.Id;
            if (conversation is not null) conversation.ContactId = contact.Id;
            await db.SaveChangesAsync(ct);
        }

        try
        {
            var appointment = await appointments.BookForAgentAsync(
                new AgentBookingRequest(ctx.Agent.CompanyId, ctx.Agent.Id, ctx.ConversationId, contactId, service.Name, startUtc), ct);
            return (true, "Agendamento registrado.", appointment);
        }
        catch (AppException ex)
        {
            return (false, ex.Message, null);
        }
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Ok(object data) => JsonSerializer.Serialize(new { ok = true, data }, Json);
    private static string Error(string message) => JsonSerializer.Serialize(new { ok = false, error = message }, Json);

    private static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
