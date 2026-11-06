using System.Text.Json;
using System.Text.RegularExpressions;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Notifications;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace K2Agents.Application.Automations;

/// <summary>Evento interno. Data contém apenas valores de exibição usados nos modelos de texto.</summary>
public record AutomationEvent(Guid CompanyId, AutomationTrigger Trigger, IReadOnlyDictionary<string, string> Data, string? Link);

/// <summary>Configuração das ações de notificação. Nenhum outro campo é aceito.</summary>
public record AutomationConfig(string? Title, string? Message);

public interface IAutomationEngine
{
    Task PublishAsync(AutomationEvent evt, CancellationToken ct);
}

/// <summary>
/// Executa regras da empresa para o gatilho recebido. Ações pertencem a um conjunto fechado; textos usam apenas
/// placeholders conhecidos. Falhas são registradas e nunca interrompem a operação que gerou o evento.
/// </summary>
public partial class AutomationEngine(
    IAppDbContext db, INotificationService notifications, IAuditService audit, ILogger<AutomationEngine> logger) : IAutomationEngine
{
    /// <summary>Placeholders disponíveis em títulos e mensagens.</summary>
    public static readonly string[] Placeholders = ["contato", "agente", "canal", "servico", "data", "conversa"];

    /// <summary>Combinações permitidas de gatilho e ação.</summary>
    public static readonly IReadOnlyDictionary<AutomationTrigger, AutomationAction[]> Allowed = new Dictionary<AutomationTrigger, AutomationAction[]>
    {
        [AutomationTrigger.LeadCreated] = [AutomationAction.CreateNotification, AutomationAction.NotifyAttendants, AutomationAction.RecordMetric],
        [AutomationTrigger.ConversationTransferred] = [AutomationAction.NotifyAttendants, AutomationAction.CreateNotification, AutomationAction.RecordMetric],
        [AutomationTrigger.AppointmentCreated] = [AutomationAction.CreateInternalConfirmation, AutomationAction.CreateNotification, AutomationAction.NotifyAttendants, AutomationAction.RecordMetric],
        [AutomationTrigger.ConversationClosed] = [AutomationAction.RecordMetric, AutomationAction.CreateNotification]
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(AutomationEvent evt, CancellationToken ct)
    {
        var rules = await db.AutomationRules.IgnoreQueryFilters()
            .Where(r => r.CompanyId == evt.CompanyId && r.TriggerType == evt.Trigger && r.Enabled)
            .ToListAsync(ct);

        foreach (var rule in rules)
        {
            try
            {
                if (!Allowed[rule.TriggerType].Contains(rule.ActionType)) continue;
                await ExecuteAsync(rule, evt, ParseConfig(rule.Configuration), ct);
                rule.ExecutionCount++;
                rule.LastExecutedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao executar a automação {RuleId}", rule.Id);
            }
        }
    }

    private async Task ExecuteAsync(AutomationRule rule, AutomationEvent evt, AutomationConfig config, CancellationToken ct)
    {
        var title = Render(config.Title ?? DefaultTitle(evt.Trigger), evt.Data);
        var message = Render(config.Message ?? string.Empty, evt.Data);

        switch (rule.ActionType)
        {
            case AutomationAction.CreateNotification:
                await notifications.CreateAsync(evt.CompanyId, TypeFor(evt.Trigger), title, message, evt.Link, null, ct);
                break;

            case AutomationAction.NotifyAttendants:
                // Uma notificação por atendente ativo; sem atendentes, avisa a empresa inteira.
                var attendants = await db.Users.IgnoreQueryFilters()
                    .Where(u => u.CompanyId == evt.CompanyId && u.IsActive && u.RoleId == UserRole.Attendant)
                    .Select(u => u.Id).ToListAsync(ct);
                if (attendants.Count == 0)
                    await notifications.CreateAsync(evt.CompanyId, TypeFor(evt.Trigger), title, message, evt.Link, null, ct);
                foreach (var userId in attendants)
                    await notifications.CreateAsync(evt.CompanyId, TypeFor(evt.Trigger), title, message, evt.Link, userId, ct);
                break;

            case AutomationAction.CreateInternalConfirmation:
                await notifications.CreateAsync(evt.CompanyId, NotificationType.Appointment,
                    Render(config.Title ?? "Confirmação interna: {servico}", evt.Data),
                    Render(config.Message ?? "{contato} — {data}. Confirme o atendimento com o cliente.", evt.Data), evt.Link, null, ct);
                audit.Record("automation.internal_confirmation", nameof(AutomationRule), rule.Id.ToString(), title, evt.CompanyId);
                break;

            case AutomationAction.RecordMetric:
                audit.Record($"metric.{evt.Trigger}", nameof(AutomationRule), rule.Id.ToString(), companyId: evt.CompanyId);
                break;
        }
    }

    /// <summary>Substitui somente {placeholders} conhecidos; qualquer outro texto permanece literal.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> data) =>
        PlaceholderPattern().Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            return Placeholders.Contains(key) && data.TryGetValue(key, out var value) ? value : m.Value;
        });

    public static AutomationConfig ParseConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new AutomationConfig(null, null);
        try
        {
            return JsonSerializer.Deserialize<AutomationConfig>(json, Json) ?? new AutomationConfig(null, null);
        }
        catch (JsonException)
        {
            return new AutomationConfig(null, null);
        }
    }

    public static string SerializeConfig(AutomationConfig config) => JsonSerializer.Serialize(config, Json);

    private static string DefaultTitle(AutomationTrigger trigger) => trigger switch
    {
        AutomationTrigger.LeadCreated => "Novo lead: {contato}",
        AutomationTrigger.ConversationTransferred => "Cliente aguardando atendimento humano",
        AutomationTrigger.AppointmentCreated => "Novo agendamento: {servico}",
        _ => "Conversa encerrada"
    };

    private static NotificationType TypeFor(AutomationTrigger trigger) => trigger switch
    {
        AutomationTrigger.LeadCreated => NotificationType.NewLead,
        AutomationTrigger.ConversationTransferred => NotificationType.Handoff,
        AutomationTrigger.AppointmentCreated => NotificationType.Appointment,
        _ => NotificationType.Info
    };

    [GeneratedRegex(@"\{([a-z]+)\}")]
    private static partial Regex PlaceholderPattern();
}

/// <summary>Regras criadas para toda empresa nova (e no seed) — reproduzem os avisos padrão da plataforma.</summary>
public static class AutomationDefaults
{
    public static IEnumerable<AutomationRule> For(Guid companyId) =>
    [
        Rule(companyId, "Avisar a equipe sobre novos leads", AutomationTrigger.LeadCreated, AutomationAction.CreateNotification,
            "Novo lead: {contato}", "Capturado pelo {canal}."),
        Rule(companyId, "Avisar atendentes sobre transferências", AutomationTrigger.ConversationTransferred, AutomationAction.NotifyAttendants,
            "Cliente aguardando atendimento humano", "Conversa com {agente} transferida para a equipe."),
        Rule(companyId, "Confirmação interna de agendamentos", AutomationTrigger.AppointmentCreated, AutomationAction.CreateInternalConfirmation,
            "Novo agendamento: {servico}", "{contato} — {data}."),
        Rule(companyId, "Registrar conversas encerradas", AutomationTrigger.ConversationClosed, AutomationAction.RecordMetric, null, null)
    ];

    private static AutomationRule Rule(Guid companyId, string name, AutomationTrigger trigger, AutomationAction action, string? title, string? message) => new()
    {
        CompanyId = companyId,
        Name = name,
        TriggerType = trigger,
        ActionType = action,
        Configuration = AutomationEngine.SerializeConfig(new AutomationConfig(title, message)),
        Enabled = true
    };
}
