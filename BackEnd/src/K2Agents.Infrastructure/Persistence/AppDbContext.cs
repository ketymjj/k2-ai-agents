using System.Linq.Expressions;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Common;
using K2Agents.Domain.Common;
using K2Agents.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
    : DbContext(options), IAppDbContext
{
    /// <summary>
    /// Empresa usada no filtro global. Nulo = sem filtro (SuperAdmin, seed, jobs e endpoints públicos,
    /// que precisam escopar a empresa explicitamente, ex.: pelo token do widget).
    /// </summary>
    public Guid? TenantFilterCompanyId =>
        currentUser.IsAuthenticated && !currentUser.IsSuperAdmin ? currentUser.CompanyId ?? Guid.Empty : null;

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AIAgent> AIAgents => Set<AIAgent>();
    public DbSet<AgentVersion> AgentVersions => Set<AgentVersion>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();
    public DbSet<ChannelConfiguration> ChannelConfigurations => Set<ChannelConfiguration>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AIUsageRecord> AIUsageRecords => Set<AIUsageRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AIBillingEntry> AIBillingEntries => Set<AIBillingEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        if (Database.IsNpgsql()) Configurations.KnowledgeChunkConfiguration.ConfigureFullTextSearch(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(ITenantEntity).IsAssignableFrom(t.ClrType)))
        {
            typeof(AppDbContext).GetMethod(nameof(ApplyTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(this, [modelBuilder]);
        }
    }

    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder) where T : class, ITenantEntity
    {
        Expression<Func<T, bool>> filter = e => TenantFilterCompanyId == null || e.CompanyId == TenantFilterCompanyId;
        modelBuilder.Entity<T>().HasQueryFilter(filter);
    }

    public async Task RunScheduleExclusiveAsync(Guid companyId, Func<CancellationToken, Task> work, CancellationToken ct)
    {
        if (!Database.IsRelational())
        {
            await work(ct);
            return;
        }
        // Chave de 64 bits derivada do Guid da empresa: trava liberada automaticamente no fim da transação.
        var key = BitConverter.ToInt64(companyId.ToByteArray(), 0);
        // Transação manual exige a estratégia de execução (EnableRetryOnFailure); numa nova tentativa, tudo é refeito.
        await Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var transaction = await Database.BeginTransactionAsync(token);
            await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", token);
            await work(token);
            await transaction.CommitAsync(token);
        }, ct);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceTenantOnWrite();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        EnforceTenantOnWrite();
        return base.SaveChanges();
    }

    /// <summary>Segunda barreira: um usuário de empresa nunca grava registros de outra empresa.</summary>
    private void EnforceTenantOnWrite()
    {
        var tenant = TenantFilterCompanyId;
        if (tenant is null) return;

        foreach (var entry in ChangeTracker.Entries<ITenantEntity>()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.State == EntityState.Added && entry.Entity.CompanyId == Guid.Empty)
                entry.Entity.CompanyId = tenant.Value;

            if (entry.Entity.CompanyId != tenant.Value)
                throw AppException.Forbidden("Operação entre empresas não permitida.");
        }
    }
}
