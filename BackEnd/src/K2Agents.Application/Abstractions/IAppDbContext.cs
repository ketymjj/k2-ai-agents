using K2Agents.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Application.Abstractions;

/// <summary>Contrato do contexto de dados usado pelos serviços da camada Application.</summary>
public interface IAppDbContext
{
    DbSet<Company> Companies { get; }
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<AIAgent> AIAgents { get; }
    DbSet<AgentVersion> AgentVersions { get; }
    DbSet<KnowledgeDocument> KnowledgeDocuments { get; }
    DbSet<KnowledgeChunk> KnowledgeChunks { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<ConversationMessage> ConversationMessages { get; }
    DbSet<Contact> Contacts { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<AutomationRule> AutomationRules { get; }
    DbSet<ChannelConfiguration> ChannelConfigurations { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<AIUsageRecord> AIUsageRecords { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<AIBillingEntry> AIBillingEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executa <paramref name="work"/> numa transação com trava exclusiva da agenda da empresa (PostgreSQL: advisory lock),
    /// para que duas reservas simultâneas não ocupem o mesmo horário. Roda dentro da estratégia de execução do provedor
    /// (compatível com retry); em provedores sem transação, apenas executa o trabalho.
    /// </summary>
    Task RunScheduleExclusiveAsync(Guid companyId, Func<CancellationToken, Task> work, CancellationToken ct);
}
