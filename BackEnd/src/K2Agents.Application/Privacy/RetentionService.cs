using K2Agents.Application.Abstractions;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace K2Agents.Application.Privacy;

public record RetentionResult(int Companies, int Conversations, int Messages);

public interface IRetentionService
{
    /// <summary>Apaga conversas cujo último contato é anterior ao prazo de retenção de cada empresa.</summary>
    Task<RetentionResult> PurgeAsync(DateTime nowUtc, CancellationToken ct);
}

/// <summary>
/// Política de retenção (LGPD): cada empresa define por quantos dias as conversas ficam guardadas
/// (Company.ConversationRetentionDays; nulo = sem exclusão automática). Conversas com atendimento humano
/// em andamento nunca são apagadas. Métricas agregadas de consumo de IA são mantidas (não contêm conteúdo).
/// </summary>
public class RetentionService(IAppDbContext db, IAuditService audit, ILogger<RetentionService> logger) : IRetentionService
{
    private const int BatchSize = 200;
    private static readonly ConversationStatus[] InProgress = [ConversationStatus.WaitingHuman, ConversationStatus.HumanActive];

    public async Task<RetentionResult> PurgeAsync(DateTime nowUtc, CancellationToken ct)
    {
        var policies = await db.Companies.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.ConversationRetentionDays != null && c.ConversationRetentionDays > 0)
            .Select(c => new { c.Id, Days = c.ConversationRetentionDays!.Value })
            .ToListAsync(ct);

        int companies = 0, conversations = 0, messages = 0;
        foreach (var policy in policies)
        {
            var cutoff = nowUtc.AddDays(-policy.Days);
            int companyConversations = 0, companyMessages = 0;
            while (true)
            {
                var batch = await db.Conversations.IgnoreQueryFilters()
                    .Where(c => c.CompanyId == policy.Id && c.LastMessageAt < cutoff && !InProgress.Contains(c.Status))
                    .OrderBy(c => c.LastMessageAt).Take(BatchSize).ToListAsync(ct);
                if (batch.Count == 0) break;

                var ids = batch.Select(c => c.Id).ToList();
                var batchMessages = await db.ConversationMessages.IgnoreQueryFilters().Where(m => ids.Contains(m.ConversationId)).ToListAsync(ct);
                var appointments = await db.Appointments.IgnoreQueryFilters().Where(a => a.ConversationId != null && ids.Contains(a.ConversationId.Value)).ToListAsync(ct);
                foreach (var a in appointments) a.ConversationId = null; // o agendamento continua; só perde o vínculo

                db.ConversationMessages.RemoveRange(batchMessages);
                db.Conversations.RemoveRange(batch);
                await db.SaveChangesAsync(ct);
                companyConversations += batch.Count;
                companyMessages += batchMessages.Count;
            }

            if (companyConversations == 0) continue;
            companies++;
            conversations += companyConversations;
            messages += companyMessages;
            audit.Record("retention.purged", "Conversation", null,
                $"{companyConversations} conversas e {companyMessages} mensagens anteriores a {cutoff:yyyy-MM-dd} (retenção de {policy.Days} dias)", policy.Id);
            await db.SaveChangesAsync(ct);
        }

        if (conversations > 0)
            logger.LogInformation("Retenção: {Conversations} conversas ({Messages} mensagens) removidas em {Companies} empresas.", conversations, messages, companies);
        return new RetentionResult(companies, conversations, messages);
    }
}
