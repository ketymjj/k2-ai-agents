using System.Globalization;
using FluentValidation;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Common;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Application.Reports;

public interface IReportService
{
    Task<DashboardDto> DashboardAsync(ReportQuery query, CancellationToken ct);
    Task<ExportFile> ExportCsvAsync(ReportExportType type, ReportQuery query, CancellationToken ct);
    Task<ExportFile> SummaryPdfAsync(ReportQuery query, CancellationToken ct);
    Task<CostReportDto> CostsAsync(Guid? companyId, string? fromMonth, string? toMonth, CancellationToken ct);
    Task<CostMonthDto> SaveBillingAsync(SaveBillingRequest request, CancellationToken ct);
    Task DeleteBillingAsync(string month, CancellationToken ct);
}

/// <summary>
/// Painel, exportações e conciliação de custos. Tudo é calculado a partir dos registros reais (conversas, mensagens,
/// contatos, agendamentos e consumo de IA); o custo de IA é ESTIMADO pela tabela de preços, e o faturado vem da fatura
/// lançada manualmente pelo SuperAdmin.
/// </summary>
public class ReportService(
    IAppDbContext db, ICurrentUser currentUser, IAuditService audit, IAIProvider provider,
    IReportPdfRenderer pdf, IValidator<SaveBillingRequest> billingValidator) : IReportService
{
    public const string TimeZoneId = "America/Sao_Paulo";
    private const int MaxDays = 366;
    private const int MaxExportRows = 50_000;

    private static readonly ConversationStatus[] HumanStatuses = [ConversationStatus.WaitingHuman, ConversationStatus.HumanActive];

    /// <summary>Nomes em português dos valores de enum exportados no CSV (os nomes em inglês são os próprios valores).</summary>
    private static readonly Dictionary<string, string> PortugueseNames = new()
    {
        ["Open"] = "Com a IA", ["WaitingHuman"] = "Aguardando humano", ["HumanActive"] = "Em atendimento", ["Closed"] = "Encerrada",
        ["New"] = "Novo", ["InService"] = "Em atendimento", ["Qualified"] = "Qualificado", ["Converted"] = "Convertido", ["Lost"] = "Perdido",
        ["Unknown"] = "Não informado", ["Low"] = "Baixo", ["Medium"] = "Médio", ["High"] = "Alto",
        ["Scheduled"] = "Agendado", ["Confirmed"] = "Confirmado", ["Cancelled"] = "Cancelado", ["Completed"] = "Concluído", ["NoShow"] = "Não compareceu",
        ["Manual"] = "Equipe", ["Agent"] = "Agente de IA", ["Simulated"] = "Simulado"
    };

    // ---------- Painel ----------

    public async Task<DashboardDto> DashboardAsync(ReportQuery query, CancellationToken ct)
    {
        var scope = Resolve(query);
        var previous = scope with { FromUtc = scope.FromUtc.AddDays(-scope.Period.Days), ToUtc = scope.FromUtc };

        var kpis = await KpisAsync(scope, ct);
        var prev = await KpisAsync(previous, ct);
        var conversations = Conversations(scope);

        var daily = await DailyAsync(scope, ct);

        var byAgentRaw = await conversations.GroupBy(c => c.AgentId).Select(g => new
        {
            AgentId = g.Key,
            Count = g.Count(),
            Transferred = g.Count(c => HumanStatuses.Contains(c.Status)
                                       || db.ConversationMessages.Any(m => m.ConversationId == c.Id && m.SenderType == SenderType.Human))
        }).ToListAsync(ct);
        var aiByAgent = await Messages(scope).Where(m => m.SenderType == SenderType.AI)
            .GroupBy(m => m.Conversation!.AgentId)
            .Select(g => new { AgentId = g.Key, Replies = g.Count(), AvgMs = g.Where(m => !m.IsSimulated && m.ResponseTimeMs != null).Average(m => (double?)m.ResponseTimeMs) })
            .ToListAsync(ct);
        var costByAgent = await Usage(scope).Where(u => u.AgentId != null).GroupBy(u => u.AgentId!.Value)
            .Select(g => new { AgentId = g.Key, Cost = g.Where(u => !u.IsSimulated).Sum(u => u.EstimatedCostUsd) })
            .ToListAsync(ct);
        var agentIds = byAgentRaw.Select(a => a.AgentId).Union(costByAgent.Select(c => c.AgentId)).ToList();
        var names = await db.AIAgents.AsNoTracking().Where(a => agentIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var byAgent = agentIds.Select(id =>
            {
                var conv = byAgentRaw.FirstOrDefault(a => a.AgentId == id);
                var ai = aiByAgent.FirstOrDefault(a => a.AgentId == id);
                return new AgentStatsDto(id, names.GetValueOrDefault(id, "—"), conv?.Count ?? 0, conv?.Transferred ?? 0, ai?.Replies ?? 0,
                    ai?.AvgMs is { } ms ? (int)Math.Round(ms) : null, costByAgent.FirstOrDefault(c => c.AgentId == id)?.Cost ?? 0m);
            })
            .OrderByDescending(a => a.Conversations).ThenBy(a => a.Name).ToList();

        var convByChannel = await conversations.GroupBy(c => c.Channel).Select(g => new { Channel = g.Key, Count = g.Count() }).ToListAsync(ct);
        // Só chamadas reais ao provedor (as simuladas não são "chamadas de IA"), igual aos indicadores.
        var usageByChannel = await Usage(scope).Where(u => !u.IsSimulated).GroupBy(u => u.Channel)
            .Select(g => new { Channel = g.Key, Calls = g.Count(), Cost = g.Sum(u => u.EstimatedCostUsd) })
            .ToListAsync(ct);
        var byChannel = Enum.GetValues<ChannelType>()
            .Select(ch => new ChannelStatsDto(ch, convByChannel.FirstOrDefault(c => c.Channel == ch)?.Count ?? 0,
                usageByChannel.FirstOrDefault(u => u.Channel == ch)?.Calls ?? 0, usageByChannel.FirstOrDefault(u => u.Channel == ch)?.Cost ?? 0m))
            .Where(c => c.Conversations > 0 || c.AICalls > 0)
            .ToList();

        var apptStatus = await Appointments(scope).GroupBy(a => a.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var convStatus = await conversations.GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);

        return new DashboardDto(scope.Period, kpis, prev, daily, byAgent, byChannel,
            Enum.GetValues<AppointmentStatus>().Select(s => new StatusCountDto(s.ToString(), apptStatus.FirstOrDefault(a => a.Key == s)?.Count ?? 0)).ToList(),
            Enum.GetValues<ConversationStatus>().Select(s => new StatusCountDto(s.ToString(), convStatus.FirstOrDefault(a => a.Key == s)?.Count ?? 0)).ToList(),
            provider.IsConfigured,
            await conversations.CountAsync(c => c.IsDemo, ct));
    }

    private async Task<KpisDto> KpisAsync(Scope scope, CancellationToken ct)
    {
        var conversations = Conversations(scope);
        var total = await conversations.CountAsync(ct);
        var closed = await conversations.CountAsync(c => c.Status == ConversationStatus.Closed, ct);
        var transferred = await conversations.CountAsync(c => HumanStatuses.Contains(c.Status)
            || db.ConversationMessages.Any(m => m.ConversationId == c.Id && m.SenderType == SenderType.Human), ct);
        var resolvedByAI = await conversations.CountAsync(c => c.Status == ConversationStatus.Closed
            && !db.ConversationMessages.Any(m => m.ConversationId == c.Id && m.SenderType == SenderType.Human), ct);

        var messages = await Messages(scope).GroupBy(m => m.SenderType).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var avgMs = await Messages(scope).Where(m => m.SenderType == SenderType.AI && !m.IsSimulated && m.ResponseTimeMs != null)
            .AverageAsync(m => (double?)m.ResponseTimeMs, ct);
        var simulated = await Messages(scope).CountAsync(m => m.SenderType == SenderType.AI && m.IsSimulated, ct);

        var contacts = Contacts(scope);
        var newContacts = await contacts.CountAsync(ct);
        var newLeads = await contacts.CountAsync(c => c.IsLead, ct);

        var appointments = Appointments(scope);
        var appointmentCount = await appointments.CountAsync(ct);
        var byAgent = await appointments.CountAsync(a => a.Source == "Agent", ct);

        var usage = Usage(scope).Where(u => !u.IsSimulated);
        var calls = await usage.CountAsync(ct);
        var failures = await usage.CountAsync(u => !u.Success, ct);
        var input = await usage.SumAsync(u => (long)u.InputTokens, ct);
        var output = await usage.SumAsync(u => (long)u.OutputTokens, ct);
        var cost = await usage.SumAsync(u => u.EstimatedCostUsd, ct);

        return new KpisDto(total, closed, resolvedByAI, transferred,
            closed > 0 ? Math.Round(100.0 * resolvedByAI / closed, 1) : null,
            Count(messages.Select(m => (m.Key, m.Count)), SenderType.Contact),
            Count(messages.Select(m => (m.Key, m.Count)), SenderType.AI),
            Count(messages.Select(m => (m.Key, m.Count)), SenderType.Human),
            avgMs is { } ms ? (int)Math.Round(ms) : null,
            newContacts, newLeads, appointmentCount, byAgent, calls, failures, simulated, input, output, Math.Round(cost, 6));
    }

    private static int Count(IEnumerable<(SenderType Key, int Count)> groups, SenderType type) =>
        groups.FirstOrDefault(g => g.Key == type).Count;

    private async Task<IReadOnlyList<DailyPointDto>> DailyAsync(Scope scope, CancellationToken ct)
    {
        // Só os instantes saem do banco; o agrupamento por dia local acontece aqui (fuso correto também no InMemory).
        var conv = await Conversations(scope).Select(c => c.StartedAt).ToListAsync(ct);
        var leads = await Contacts(scope).Where(c => c.IsLead).Select(c => c.CreatedAt).ToListAsync(ct);
        var appts = await Appointments(scope).Select(a => a.CreatedAt).ToListAsync(ct);
        var usage = await Usage(scope).Where(u => !u.IsSimulated).Select(u => new { u.CreatedAt, u.EstimatedCostUsd }).ToListAsync(ct);

        DateOnly Day(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));
        var convByDay = conv.GroupBy(Day).ToDictionary(g => g.Key, g => g.Count());
        var leadsByDay = leads.GroupBy(Day).ToDictionary(g => g.Key, g => g.Count());
        var apptsByDay = appts.GroupBy(Day).ToDictionary(g => g.Key, g => g.Count());
        var usageByDay = usage.GroupBy(u => Day(u.CreatedAt)).ToDictionary(g => g.Key, g => (Calls: g.Count(), Cost: g.Sum(u => u.EstimatedCostUsd)));

        return Enumerable.Range(0, scope.Period.Days).Select(i =>
        {
            var d = scope.Period.From.AddDays(i);
            var u = usageByDay.GetValueOrDefault(d);
            return new DailyPointDto(d, convByDay.GetValueOrDefault(d), leadsByDay.GetValueOrDefault(d), apptsByDay.GetValueOrDefault(d), u.Calls, Math.Round(u.Cost, 6));
        }).ToList();
    }

    // ---------- Exportações ----------

    public async Task<ExportFile> ExportCsvAsync(ReportExportType type, ReportQuery query, CancellationToken ct)
    {
        var scope = Resolve(query);
        var pt = !string.Equals(query.Lang, "en", StringComparison.OrdinalIgnoreCase);
        var csv = new CsvWriter(pt);
        string L(string ptText, string enText) => pt ? ptText : enText;
        string E(object value) => pt && PortugueseNames.TryGetValue(value.ToString()!, out var name) ? name : value.ToString()!;
        var withCompany = currentUser.IsSuperAdmin;
        object?[] Prefix(string? company, params object?[] values) => withCompany ? [company, .. values] : values;
        var companyHeader = L("Empresa", "Company");

        switch (type)
        {
            case ReportExportType.Conversations:
                csv.Row(Prefix(companyHeader, L("Início", "Started"), L("Canal", "Channel"), L("Agente", "Agent"), L("Contato", "Contact"),
                    "Status", L("Mensagens", "Messages"), L("Transferida para humano", "Handed to human"), L("Encerrada em", "Closed at")));
                var conversations = await Conversations(scope).OrderBy(c => c.StartedAt).Take(MaxExportRows).Select(c => new
                {
                    Company = db.Companies.Where(x => x.Id == c.CompanyId).Select(x => x.Name).FirstOrDefault(),
                    c.StartedAt, c.Channel, Agent = c.Agent!.Name, Contact = c.Contact != null ? c.Contact.Name : null, c.Status,
                    Messages = db.ConversationMessages.Count(m => m.ConversationId == c.Id),
                    Human = HumanStatuses.Contains(c.Status) || db.ConversationMessages.Any(m => m.ConversationId == c.Id && m.SenderType == SenderType.Human),
                    c.ClosedAt
                }).ToListAsync(ct);
                foreach (var c in conversations)
                    csv.Row(Prefix(c.Company, ToLocal(c.StartedAt), c.Channel.ToString(), c.Agent, c.Contact, E(c.Status), c.Messages, c.Human,
                        c.ClosedAt is { } closedAt ? ToLocal(closedAt) : null));
                break;

            case ReportExportType.Leads:
                csv.Row(Prefix(companyHeader, L("Criado em", "Created"), L("Nome", "Name"), "E-mail", L("Telefone", "Phone"), L("Lead", "Lead"),
                    L("Etapa", "Stage"), L("Interesse", "Interest"), L("Origem", "Source")));
                var contacts = await Contacts(scope).OrderBy(c => c.CreatedAt).Take(MaxExportRows).Select(c => new
                {
                    Company = db.Companies.Where(x => x.Id == c.CompanyId).Select(x => x.Name).FirstOrDefault(),
                    c.CreatedAt, c.Name, c.Email, c.Phone, c.IsLead, c.Status, c.Interest, c.Source, c.IsAnonymized
                }).ToListAsync(ct);
                foreach (var c in contacts)
                    csv.Row(Prefix(c.Company, ToLocal(c.CreatedAt), c.Name, c.IsAnonymized ? null : c.Email, c.IsAnonymized ? null : c.Phone,
                        c.IsLead, E(c.Status), E(c.Interest), c.Source));
                break;

            case ReportExportType.Appointments:
                csv.Row(Prefix(companyHeader, L("Início", "Start"), L("Fim", "End"), L("Serviço", "Service"), "Status", L("Cliente", "Customer"),
                    L("Telefone", "Phone"), L("Origem", "Source"), L("Agente", "Agent"), L("Criado em", "Created")));
                var appointments = await Appointments(scope).OrderBy(a => a.StartDateTime).Take(MaxExportRows).Select(a => new
                {
                    Company = db.Companies.Where(x => x.Id == a.CompanyId).Select(x => x.Name).FirstOrDefault(),
                    a.StartDateTime, a.EndDateTime, a.ServiceName, a.Status,
                    Contact = a.Contact != null ? a.Contact.Name : null,
                    Phone = a.Contact != null && !a.Contact.IsAnonymized ? a.Contact.Phone : null,
                    a.Source, Agent = a.Agent != null ? a.Agent.Name : null, a.CreatedAt
                }).ToListAsync(ct);
                foreach (var a in appointments)
                    csv.Row(Prefix(a.Company, ToLocal(a.StartDateTime), ToLocal(a.EndDateTime), a.ServiceName, E(a.Status), a.Contact, a.Phone,
                        E(a.Source), a.Agent, ToLocal(a.CreatedAt)));
                break;

            case ReportExportType.AIUsage:
                csv.Row(Prefix(companyHeader, L("Data", "Date"), L("Agente", "Agent"), L("Canal", "Channel"), L("Provedor", "Provider"), L("Modelo", "Model"),
                    L("Tokens de entrada", "Input tokens"), L("Tokens de saída", "Output tokens"), L("Custo estimado (US$)", "Estimated cost (USD)"),
                    L("Latência (ms)", "Latency (ms)"), L("Sucesso", "Success"), L("Erro", "Error"), L("Simulado", "Simulated")));
                var usage = await Usage(scope).OrderBy(u => u.CreatedAt).Take(MaxExportRows).Select(u => new
                {
                    Company = db.Companies.Where(x => x.Id == u.CompanyId).Select(x => x.Name).FirstOrDefault(),
                    Agent = db.AIAgents.Where(x => x.Id == u.AgentId).Select(x => x.Name).FirstOrDefault(),
                    u.CreatedAt, u.Channel, u.Provider, u.Model, u.InputTokens, u.OutputTokens, u.EstimatedCostUsd, u.LatencyMs, u.Success, u.ErrorCode, u.IsSimulated
                }).ToListAsync(ct);
                foreach (var u in usage)
                    csv.Row(Prefix(u.Company, ToLocal(u.CreatedAt), u.Agent, u.Channel.ToString(), u.Provider, u.Model, u.InputTokens, u.OutputTokens,
                        u.EstimatedCostUsd, u.LatencyMs, u.Success, u.ErrorCode, u.IsSimulated));
                break;
        }

        audit.Record("report.exported", "Report", type.ToString(), $"{scope.Period.From:yyyy-MM-dd}..{scope.Period.To:yyyy-MM-dd}", scope.CompanyId);
        await db.SaveChangesAsync(ct);
        var name = type switch
        {
            ReportExportType.Conversations => L("conversas", "conversations"),
            ReportExportType.Leads => L("contatos", "contacts"),
            ReportExportType.Appointments => L("agendamentos", "appointments"),
            _ => L("consumo-ia", "ai-usage")
        };
        return new ExportFile($"k2-{name}-{scope.Period.From:yyyyMMdd}-{scope.Period.To:yyyyMMdd}.csv", "text/csv; charset=utf-8", csv.ToBytes());
    }

    public async Task<ExportFile> SummaryPdfAsync(ReportQuery query, CancellationToken ct)
    {
        var d = await DashboardAsync(query, ct);
        var pt = !string.Equals(query.Lang, "en", StringComparison.OrdinalIgnoreCase);
        var culture = CultureInfo.GetCultureInfo(pt ? "pt-BR" : "en-US");
        string L(string ptText, string enText) => pt ? ptText : enText;
        string N(long v) => v.ToString("N0", culture);
        string Usd(decimal v) => "US$ " + v.ToString("N4", culture);
        string Delta(double now, double before) => before == 0 ? "—" : ((now - before) / before * 100).ToString("+0.#;-0.#;0", culture) + "%";

        var scope = Resolve(query);
        var companyName = scope.CompanyId is { } cid
            ? await db.Companies.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(ct)
            : L("Todas as empresas", "All companies");
        var agentName = query.AgentId is { } aid ? await db.AIAgents.Where(a => a.Id == aid).Select(a => a.Name).FirstOrDefaultAsync(ct) : null;

        var k = d.Kpis;
        var p = d.Previous;
        var kpiRows = new List<IReadOnlyList<string>>
        {
            new[] { L("Conversas", "Conversations"), N(k.Conversations), N(p.Conversations), Delta(k.Conversations, p.Conversations) },
            new[] { L("Resolvidas pela IA", "Resolved by AI"), N(k.ResolvedByAI), N(p.ResolvedByAI), Delta(k.ResolvedByAI, p.ResolvedByAI) },
            new[] { L("Transferidas para humano", "Handed to human"), N(k.Transferred), N(p.Transferred), Delta(k.Transferred, p.Transferred) },
            new[] { L("Taxa de resolução pela IA", "AI resolution rate"), k.ResolutionRate is { } r ? r.ToString("0.#", culture) + "%" : "—",
                p.ResolutionRate is { } pr ? pr.ToString("0.#", culture) + "%" : "—", "" },
            new[] { L("Novos contatos", "New contacts"), N(k.NewContacts), N(p.NewContacts), Delta(k.NewContacts, p.NewContacts) },
            new[] { L("Novos leads", "New leads"), N(k.NewLeads), N(p.NewLeads), Delta(k.NewLeads, p.NewLeads) },
            new[] { L("Agendamentos", "Appointments"), N(k.Appointments), N(p.Appointments), Delta(k.Appointments, p.Appointments) },
            new[] { L("Agendados pelo agente", "Booked by agent"), N(k.AppointmentsByAgent), N(p.AppointmentsByAgent), Delta(k.AppointmentsByAgent, p.AppointmentsByAgent) },
            new[] { L("Tempo médio de resposta da IA", "Avg. AI response time"), k.AvgAIResponseMs is { } ms ? N(ms) + " ms" : "—",
                p.AvgAIResponseMs is { } pms ? N(pms) + " ms" : "—", "" },
            new[] { L("Chamadas de IA (reais)", "AI calls (real)"), N(k.AICalls), N(p.AICalls), Delta(k.AICalls, p.AICalls) },
            new[] { L("Respostas simuladas", "Simulated replies"), N(k.SimulatedReplies), N(p.SimulatedReplies), "" },
            new[] { L("Tokens (entrada + saída)", "Tokens (input + output)"), N(k.InputTokens + k.OutputTokens), N(p.InputTokens + p.OutputTokens), "" },
            new[] { L("Custo estimado de IA", "Estimated AI cost"), Usd(k.EstimatedCostUsd), Usd(p.EstimatedCostUsd), Delta((double)k.EstimatedCostUsd, (double)p.EstimatedCostUsd) }
        };

        var sections = new List<PdfSection>
        {
            new(L("Indicadores", "Key metrics"), [L("Indicador", "Metric"), L("Período", "Period"), L("Período anterior", "Previous period"), L("Variação", "Change")],
                kpiRows, [0.46f, 0.18f, 0.2f, 0.16f]),
            new(L("Por agente", "By agent"),
                [L("Agente", "Agent"), L("Conversas", "Conversations"), L("Transferidas", "Handed off"), L("Respostas IA", "AI replies"), L("Tempo médio", "Avg. time"), L("Custo est.", "Est. cost")],
                d.ByAgent.Select(a => (IReadOnlyList<string>)new[] { a.Name, N(a.Conversations), N(a.Transferred), N(a.AIReplies),
                    a.AvgAIResponseMs is { } ams ? N(ams) + " ms" : "—", Usd(a.EstimatedCostUsd) }).ToList(),
                [0.28f, 0.14f, 0.14f, 0.14f, 0.13f, 0.17f]),
            new(L("Por canal", "By channel"), [L("Canal", "Channel"), L("Conversas", "Conversations"), L("Chamadas de IA", "AI calls"), L("Custo est.", "Est. cost")],
                d.ByChannel.Select(c => (IReadOnlyList<string>)new[] { ChannelName(c.Channel), N(c.Conversations), N(c.AICalls), Usd(c.EstimatedCostUsd) }).ToList(),
                [0.4f, 0.2f, 0.2f, 0.2f])
        };

        var subtitle = new List<string>
        {
            $"{companyName}{(agentName is null ? "" : " · " + L("Agente", "Agent") + ": " + agentName)}{(query.Channel is { } ch ? " · " + ChannelName(ch) : "")}",
            $"{L("Período", "Period")}: {d.Period.From.ToString("d", culture)} – {d.Period.To.ToString("d", culture)} ({d.Period.Days} {L("dias", "days")}, {d.Period.TimeZone})",
            $"{L("Gerado em", "Generated")}: {ToLocal(DateTime.UtcNow).ToString("g", culture)}"
        };
        if (d.DemoConversations > 0)
            subtitle.Add(L($"Atenção: {d.DemoConversations} de {k.Conversations} conversas do período são dados de demonstração.",
                $"Note: {d.DemoConversations} of {k.Conversations} conversations in this period are demo data."));
        var footer = L("Custos de IA são estimados pela tabela de preços configurada; o valor faturado consta da fatura do provedor. "
                       + "Respostas simuladas (modo demonstrativo) não geram custo.",
            "AI costs are estimated from the configured price table; the billed amount is on the provider's invoice. "
            + "Simulated replies (demo mode) have no cost.");

        var bytes = pdf.Render(new PdfReport(L("Relatório de atendimento — K2 AI Agents", "Customer service report — K2 AI Agents"), subtitle, sections, footer));
        audit.Record("report.exported", "Report", "SummaryPdf", $"{d.Period.From:yyyy-MM-dd}..{d.Period.To:yyyy-MM-dd}", scope.CompanyId);
        await db.SaveChangesAsync(ct);
        return new ExportFile($"k2-{L("relatorio", "report")}-{d.Period.From:yyyyMMdd}-{d.Period.To:yyyyMMdd}.pdf", "application/pdf", bytes);
    }

    // ---------- Custos ----------

    public async Task<CostReportDto> CostsAsync(Guid? companyId, string? fromMonth, string? toMonth, CancellationToken ct)
    {
        var company = ResolveCompany(companyId);
        var nowLocal = ToLocal(DateTime.UtcNow);
        var to = TryParseMonth(toMonth, out var t) ? t : new DateOnly(nowLocal.Year, nowLocal.Month, 1);
        var from = TryParseMonth(fromMonth, out var f) ? f : to.AddMonths(-11);
        if (from > to) throw new AppException("O mês inicial deve ser anterior ao final.", 400, "invalid_period");
        if (from < to.AddMonths(-35)) throw new AppException("Consulte no máximo 36 meses.", 400, "period_too_long");

        var fromUtc = ToUtc(from.ToDateTime(TimeOnly.MinValue));
        var toUtc = ToUtc(to.AddMonths(1).ToDateTime(TimeOnly.MinValue));
        var usage = db.AIUsageRecords.AsNoTracking().Where(u => !u.IsSimulated && u.CreatedAt >= fromUtc && u.CreatedAt < toUtc);
        if (company.HasValue) usage = usage.Where(u => u.CompanyId == company);

        var rows = await usage.Select(u => new { u.CompanyId, u.CreatedAt, Tokens = (long)u.InputTokens + u.OutputTokens, u.EstimatedCostUsd }).ToListAsync(ct);

        // O faturado é da conta da plataforma: só faz sentido comparar no total, visto pelo SuperAdmin.
        var canSeeBilling = currentUser.IsSuperAdmin && !company.HasValue;
        var billing = canSeeBilling
            ? await db.AIBillingEntries.AsNoTracking().Where(b => b.Month >= from && b.Month <= to).ToListAsync(ct)
            : [];

        var months = new List<CostMonthDto>();
        for (var m = from; m <= to; m = m.AddMonths(1))
        {
            var month = m;
            var inMonth = rows.Where(r => { var l = ToLocal(r.CreatedAt); return l.Year == month.Year && l.Month == month.Month; }).ToList();
            var estimated = Math.Round(inMonth.Sum(r => r.EstimatedCostUsd), 6);
            var billed = billing.Where(b => b.Month == month).ToList();
            decimal? billedTotal = billed.Count > 0 ? billed.Sum(b => b.AmountUsd) : null;
            decimal? diff = billedTotal is { } bt ? Math.Round(bt - estimated, 6) : null;
            double? pct = billedTotal is { } b2 && estimated > 0 ? Math.Round((double)((b2 - estimated) / estimated * 100), 1) : null;
            months.Add(new CostMonthDto(month.ToString("yyyy-MM", CultureInfo.InvariantCulture), inMonth.Count, inMonth.Sum(r => r.Tokens),
                estimated, billedTotal, billed.Select(b => b.Notes).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)), diff, pct));
        }

        var byCompanyRaw = rows.GroupBy(r => r.CompanyId)
            .Select(g => new { CompanyId = g.Key, Calls = g.Count(), Tokens = g.Sum(r => r.Tokens), Cost = g.Sum(r => r.EstimatedCostUsd) }).ToList();
        var ids = byCompanyRaw.Select(c => c.CompanyId).ToList();
        var names = await db.Companies.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var byCompany = byCompanyRaw
            .Select(c => new CompanyCostDto(c.CompanyId, names.GetValueOrDefault(c.CompanyId, "—"), c.Calls, c.Tokens, Math.Round(c.Cost, 6)))
            .OrderByDescending(c => c.EstimatedUsd).ToList();

        return new CostReportDto(months, byCompany, Math.Round(months.Sum(m => m.EstimatedUsd), 6),
            canSeeBilling && months.Any(m => m.BilledUsd.HasValue) ? months.Sum(m => m.BilledUsd ?? 0m) : null, canSeeBilling);
    }

    public async Task<CostMonthDto> SaveBillingAsync(SaveBillingRequest request, CancellationToken ct)
    {
        if (!currentUser.IsSuperAdmin) throw AppException.Forbidden();
        await billingValidator.ValidateAndThrowAsync(request, ct);
        TryParseMonth(request.Month, out var month);
        var providerName = string.IsNullOrWhiteSpace(request.Provider) ? "OpenAI" : request.Provider.Trim();

        var entry = await db.AIBillingEntries.FirstOrDefaultAsync(b => b.Month == month && b.Provider == providerName, ct);
        if (entry is null)
        {
            entry = new AIBillingEntry { Month = month, Provider = providerName };
            db.AIBillingEntries.Add(entry);
        }
        entry.AmountUsd = Math.Round(request.AmountUsd, 2);
        entry.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        entry.CreatedByUserId = currentUser.UserId;
        entry.UpdatedAt = DateTime.UtcNow;
        audit.Record("billing.saved", nameof(AIBillingEntry), request.Month, $"{providerName}: US$ {entry.AmountUsd.ToString(CultureInfo.InvariantCulture)}");
        await db.SaveChangesAsync(ct);

        var report = await CostsAsync(null, request.Month, request.Month, ct);
        return report.Months[0];
    }

    public async Task DeleteBillingAsync(string month, CancellationToken ct)
    {
        if (!currentUser.IsSuperAdmin) throw AppException.Forbidden();
        if (!TryParseMonth(month, out var m)) throw new AppException("Informe o mês no formato AAAA-MM.", 400, "invalid_month");
        var entries = await db.AIBillingEntries.Where(b => b.Month == m).ToListAsync(ct);
        if (entries.Count == 0) throw AppException.NotFound("Lançamento");
        db.AIBillingEntries.RemoveRange(entries);
        audit.Record("billing.deleted", nameof(AIBillingEntry), month);
        await db.SaveChangesAsync(ct);
    }

    private static string ChannelName(ChannelType channel) => channel == ChannelType.WebChat ? "Web Chat" : channel.ToString();

    public static bool TryParseMonth(string? value, out DateOnly month)
    {
        month = default;
        if (string.IsNullOrWhiteSpace(value)
            || !DateOnly.TryParseExact(value.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            || parsed.Year < 2020 || parsed.Year > 2100) return false;
        month = parsed;
        return true;
    }

    // ---------- Escopo e consultas base ----------

    private sealed record Scope(Guid? CompanyId, Guid? AgentId, ChannelType? Channel, DateTime FromUtc, DateTime ToUtc, ReportPeriod Period);

    private Scope Resolve(ReportQuery query)
    {
        var company = ResolveCompany(query.CompanyId);
        var today = DateOnly.FromDateTime(ToLocal(DateTime.UtcNow));
        var to = query.To ?? today;
        var from = query.From ?? to.AddDays(-29);
        if (from > to) throw new AppException("A data inicial deve ser anterior à final.", 400, "invalid_period");
        var days = to.DayNumber - from.DayNumber + 1;
        if (days > MaxDays) throw new AppException($"Consulte no máximo {MaxDays} dias.", 400, "period_too_long");
        return new Scope(company, query.AgentId, query.Channel,
            ToUtc(from.ToDateTime(TimeOnly.MinValue)), ToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue)),
            new ReportPeriod(from, to, days, TimeZoneId));
    }

    /// <summary>SuperAdmin escolhe a empresa (ou vê a plataforma toda); os demais ficam sempre na própria empresa.</summary>
    private Guid? ResolveCompany(Guid? requested)
    {
        if (currentUser.IsSuperAdmin) return requested;
        if (requested.HasValue && requested != currentUser.CompanyId) throw AppException.Forbidden();
        return currentUser.CompanyId ?? throw AppException.Forbidden();
    }

    private IQueryable<Conversation> Conversations(Scope s)
    {
        var q = db.Conversations.AsNoTracking().Where(c => c.StartedAt >= s.FromUtc && c.StartedAt < s.ToUtc);
        if (s.CompanyId.HasValue) q = q.Where(c => c.CompanyId == s.CompanyId);
        if (s.AgentId.HasValue) q = q.Where(c => c.AgentId == s.AgentId);
        q = s.Channel.HasValue ? q.Where(c => c.Channel == s.Channel) : q.Where(c => c.Channel != ChannelType.Playground);
        return q;
    }

    /// <summary>Mensagens enviadas no período, nas conversas que passam pelos filtros de empresa/agente/canal.</summary>
    private IQueryable<ConversationMessage> Messages(Scope s)
    {
        var q = db.ConversationMessages.AsNoTracking().Where(m => m.CreatedAt >= s.FromUtc && m.CreatedAt < s.ToUtc);
        if (s.CompanyId.HasValue) q = q.Where(m => m.CompanyId == s.CompanyId);
        if (s.AgentId.HasValue) q = q.Where(m => m.Conversation!.AgentId == s.AgentId);
        q = s.Channel.HasValue ? q.Where(m => m.Conversation!.Channel == s.Channel) : q.Where(m => m.Conversation!.Channel != ChannelType.Playground);
        return q;
    }

    /// <summary>Contatos criados no período. Agente e canal não se aplicam (o contato é da empresa).</summary>
    private IQueryable<Contact> Contacts(Scope s)
    {
        var q = db.Contacts.AsNoTracking().Where(c => c.CreatedAt >= s.FromUtc && c.CreatedAt < s.ToUtc);
        if (s.CompanyId.HasValue) q = q.Where(c => c.CompanyId == s.CompanyId);
        return q;
    }

    /// <summary>Agendamentos criados no período.</summary>
    private IQueryable<Appointment> Appointments(Scope s)
    {
        var q = db.Appointments.AsNoTracking().Where(a => a.CreatedAt >= s.FromUtc && a.CreatedAt < s.ToUtc);
        if (s.CompanyId.HasValue) q = q.Where(a => a.CompanyId == s.CompanyId);
        if (s.AgentId.HasValue) q = q.Where(a => a.AgentId == s.AgentId);
        return q;
    }

    /// <summary>Consumo de IA no período. Sem filtro de canal inclui o Playground: testes também geram custo real.</summary>
    private IQueryable<AIUsageRecord> Usage(Scope s)
    {
        var q = db.AIUsageRecords.AsNoTracking().Where(u => u.CreatedAt >= s.FromUtc && u.CreatedAt < s.ToUtc);
        if (s.CompanyId.HasValue) q = q.Where(u => u.CompanyId == s.CompanyId);
        if (s.AgentId.HasValue) q = q.Where(u => u.AgentId == s.AgentId);
        if (s.Channel.HasValue) q = q.Where(u => u.Channel == s.Channel);
        return q;
    }

    private static readonly TimeZoneInfo Zone = FindZone();

    private static TimeZoneInfo FindZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId); }
        catch { return TimeZoneInfo.Utc; }
    }

    private static DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);
    private static DateTime ToUtc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);
}
