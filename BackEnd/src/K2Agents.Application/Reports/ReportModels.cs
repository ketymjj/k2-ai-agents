using FluentValidation;
using K2Agents.Domain.Enums;

namespace K2Agents.Application.Reports;

/// <summary>Filtros comuns. Datas são dias locais (fuso da plataforma), ambos inclusivos.</summary>
public record ReportQuery
{
    public Guid? CompanyId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public Guid? AgentId { get; init; }

    /// <summary>Sem canal, as métricas de atendimento ignoram o Playground (testes internos).</summary>
    public ChannelType? Channel { get; init; }

    /// <summary>Idioma dos cabeçalhos das exportações ("pt-BR" ou "en").</summary>
    public string? Lang { get; init; }
}

public record ReportPeriod(DateOnly From, DateOnly To, int Days, string TimeZone);

public record KpisDto(
    int Conversations,
    int ClosedConversations,
    int ResolvedByAI,
    int Transferred,
    double? ResolutionRate,
    int CustomerMessages,
    int AIReplies,
    int HumanReplies,
    int? AvgAIResponseMs,
    int NewContacts,
    int NewLeads,
    int Appointments,
    int AppointmentsByAgent,
    int AICalls,
    int AIFailures,
    int SimulatedReplies,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedCostUsd);

public record DailyPointDto(DateOnly Date, int Conversations, int Leads, int Appointments, int AICalls, decimal EstimatedCostUsd);

public record AgentStatsDto(Guid AgentId, string Name, int Conversations, int Transferred, int AIReplies, int? AvgAIResponseMs, decimal EstimatedCostUsd);

public record ChannelStatsDto(ChannelType Channel, int Conversations, int AICalls, decimal EstimatedCostUsd);

public record StatusCountDto(string Status, int Count);

public record DashboardDto(
    ReportPeriod Period,
    KpisDto Kpis,
    KpisDto Previous,
    IReadOnlyList<DailyPointDto> Daily,
    IReadOnlyList<AgentStatsDto> ByAgent,
    IReadOnlyList<ChannelStatsDto> ByChannel,
    IReadOnlyList<StatusCountDto> AppointmentsByStatus,
    IReadOnlyList<StatusCountDto> ConversationsByStatus,
    bool AIConfigured,
    int DemoConversations);

// ---------- Custos: estimado x faturado ----------

public record CostMonthDto(
    string Month,
    int AICalls,
    long Tokens,
    decimal EstimatedUsd,
    decimal? BilledUsd,
    string? BilledNotes,
    decimal? DifferenceUsd,
    double? DifferencePct);

public record CompanyCostDto(Guid CompanyId, string Name, int AICalls, long Tokens, decimal EstimatedUsd);

public record CostReportDto(
    IReadOnlyList<CostMonthDto> Months,
    IReadOnlyList<CompanyCostDto> ByCompany,
    decimal TotalEstimatedUsd,
    decimal? TotalBilledUsd,
    bool CanSeeBilling);

public record SaveBillingRequest(string Month, decimal AmountUsd, string? Notes, string? Provider);

public class SaveBillingRequestValidator : AbstractValidator<SaveBillingRequest>
{
    public SaveBillingRequestValidator()
    {
        RuleFor(x => x.Month).Must(m => ReportService.TryParseMonth(m, out _)).WithMessage("Informe o mês no formato AAAA-MM.");
        RuleFor(x => x.AmountUsd).GreaterThanOrEqualTo(0).LessThan(10_000_000).WithMessage("Informe um valor válido.");
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Provider).MaximumLength(40);
    }
}

// ---------- Exportação ----------

public enum ReportExportType { Conversations, Leads, Appointments, AIUsage }

public record ExportFile(string FileName, string ContentType, byte[] Content);

/// <summary>Documento genérico (título, linhas de resumo e tabelas) desenhado em PDF pela infraestrutura.</summary>
public record PdfReport(string Title, IReadOnlyList<string> Subtitle, IReadOnlyList<PdfSection> Sections, string Footer);

public record PdfSection(string Heading, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<float>? Widths = null);

public interface IReportPdfRenderer
{
    byte[] Render(PdfReport report);
}
