using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace K2Agents.Infrastructure.Persistence.Configurations;

// Enums são gravados como texto para que o banco seja legível e estável a reordenações.

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> b)
    {
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.Document).HasMaxLength(32);
        b.Property(x => x.Email).HasMaxLength(160);
        b.Property(x => x.Phone).HasMaxLength(32);
        b.Property(x => x.Industry).HasMaxLength(80);
        b.Property(x => x.Website).HasMaxLength(255);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.SchedulingJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        b.HasIndex(x => x.Document).IsUnique().HasFilter("\"Document\" IS NOT NULL");
        b.HasIndex(x => x.Name);
    }
}

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(40).IsRequired();
        b.Property(x => x.Description).HasMaxLength(200);
        b.HasData(
            new Role { Id = UserRole.SuperAdmin, Name = "SUPER_ADMIN", Description = "Administração da plataforma" },
            new Role { Id = UserRole.CompanyAdmin, Name = "COMPANY_ADMIN", Description = "Gestão dos dados da própria empresa" },
            new Role { Id = UserRole.AgentManager, Name = "AGENT_MANAGER", Description = "Configuração dos agentes" },
            new Role { Id = UserRole.Attendant, Name = "ATTENDANT", Description = "Atendimento humano" },
            new Role { Id = UserRole.Viewer, Name = "VIEWER", Description = "Consulta autorizada" });
    }
}

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Email).HasMaxLength(160).IsRequired();
        b.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        b.Property(x => x.PreferredLanguage).HasMaxLength(10);
        b.HasIndex(x => x.Email).IsUnique();
        b.HasIndex(x => x.CompanyId);
        b.HasOne(x => x.Company).WithMany(c => c.Users).HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.IsActive);
    }
}

public class AIAgentConfiguration : IEntityTypeConfiguration<AIAgent>
{
    public void Configure(EntityTypeBuilder<AIAgent> b)
    {
        b.ToTable("AIAgents");
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.BusinessType).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.SystemPrompt).HasMaxLength(20000);
        b.Property(x => x.WelcomeMessage).HasMaxLength(1000);
        b.Property(x => x.Model).HasMaxLength(60);
        b.Property(x => x.Temperature).HasPrecision(3, 2);
        b.Property(x => x.BehaviorJson).HasColumnType("jsonb");
        b.Property(x => x.ResponseLanguage).HasMaxLength(10);
        b.HasIndex(x => new { x.CompanyId, x.Status });
        b.HasOne(x => x.Company).WithMany(c => c.Agents).HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AgentVersionConfiguration : IEntityTypeConfiguration<AgentVersion>
{
    public void Configure(EntityTypeBuilder<AgentVersion> b)
    {
        b.Property(x => x.SystemPrompt).HasMaxLength(20000);
        b.Property(x => x.BehaviorJson).HasColumnType("jsonb");
        b.Property(x => x.Model).HasMaxLength(60);
        b.Property(x => x.Temperature).HasPrecision(3, 2);
        b.Property(x => x.WelcomeMessage).HasMaxLength(1000);
        b.Property(x => x.ChangeNote).HasMaxLength(300);
        b.HasIndex(x => new { x.AgentId, x.VersionNumber }).IsUnique();
        b.HasOne(x => x.Agent).WithMany(a => a.Versions).HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> b)
    {
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Content).IsRequired();
        b.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.FilePath).HasMaxLength(500);
        b.Property(x => x.OriginalFileName).HasMaxLength(255);
        b.Property(x => x.ErrorMessage).HasMaxLength(500);
        b.HasIndex(x => new { x.CompanyId, x.AgentId, x.Status });
        b.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class KnowledgeChunkConfiguration : IEntityTypeConfiguration<KnowledgeChunk>
{
    public const string SearchVector = "SearchVector";

    public void Configure(EntityTypeBuilder<KnowledgeChunk> b)
    {
        b.Property(x => x.Content).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.AgentId });
        b.HasIndex(x => new { x.DocumentId, x.ChunkIndex }).IsUnique();
        b.HasOne(x => x.Document).WithMany(d => d.Chunks).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>
    /// Coluna gerada pelo PostgreSQL (dicionário português: remove stopwords e reduz palavras ao radical),
    /// sem acentos — "pao frances" encontra "pão francês". immutable_unaccent é criada pela migration.
    /// Propriedade sombra para não acoplar o domínio ao Npgsql; aplicada apenas quando o provedor é PostgreSQL.
    /// </summary>
    public static void ConfigureFullTextSearch(ModelBuilder modelBuilder)
    {
        var b = modelBuilder.Entity<KnowledgeChunk>();
        b.Property<NpgsqlTypes.NpgsqlTsVector>(SearchVector)
            .HasComputedColumnSql("to_tsvector('portuguese', immutable_unaccent(\"Content\"))", stored: true);
        b.HasIndex(SearchVector).HasMethod("GIN");
    }
}

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> b)
    {
        b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ExternalSessionId).HasMaxLength(120);
        b.HasIndex(x => new { x.CompanyId, x.Status, x.LastMessageAt });
        b.HasIndex(x => new { x.CompanyId, x.AgentId, x.StartedAt });
        b.HasIndex(x => new { x.AgentId, x.Channel, x.ExternalSessionId });
        b.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Contact).WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.AssignedUser).WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    public void Configure(EntityTypeBuilder<ConversationMessage> b)
    {
        b.Property(x => x.SenderType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Content).HasMaxLength(8000).IsRequired();
        b.Property(x => x.ExternalMessageId).HasMaxLength(160);
        b.Property(x => x.KnowledgeSources).HasColumnType("jsonb");
        b.HasIndex(x => new { x.ConversationId, x.CreatedAt });
        // Idempotência: a mesma mensagem externa (webhook reenviado) não é gravada duas vezes.
        b.HasIndex(x => new { x.CompanyId, x.ExternalMessageId }).IsUnique().HasFilter("\"ExternalMessageId\" IS NOT NULL");
        b.HasOne(x => x.Conversation).WithMany(c => c.Messages).HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> b)
    {
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Email).HasMaxLength(160);
        b.Property(x => x.Phone).HasMaxLength(32);
        b.Property(x => x.Source).HasMaxLength(40);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Interest).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.CompanyId, x.Status });
        b.HasIndex(x => new { x.CompanyId, x.Email });
        b.HasIndex(x => new { x.CompanyId, x.Phone });
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> b)
    {
        b.Property(x => x.ServiceName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Source).HasMaxLength(20);
        b.HasIndex(x => x.ConversationId);
        b.HasIndex(x => new { x.CompanyId, x.StartDateTime, x.EndDateTime });
        b.HasOne(x => x.Contact).WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AutomationRuleConfiguration : IEntityTypeConfiguration<AutomationRule>
{
    public void Configure(EntityTypeBuilder<AutomationRule> b)
    {
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.TriggerType).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.ActionType).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.Configuration).HasColumnType("jsonb");
        b.HasIndex(x => new { x.CompanyId, x.TriggerType, x.Enabled });
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ChannelConfigurationConfiguration : IEntityTypeConfiguration<ChannelConfiguration>
{
    public void Configure(EntityTypeBuilder<ChannelConfiguration> b)
    {
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.ChannelType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.PublicToken).HasMaxLength(64);
        b.Property(x => x.SettingsJson).HasColumnType("jsonb");
        b.Property(x => x.AllowedOrigins).HasMaxLength(1000);
        b.HasIndex(x => x.PublicToken).IsUnique().HasFilter("\"PublicToken\" IS NOT NULL");
        b.Property(x => x.ExternalAccountId).HasMaxLength(64);
        // Um mesmo número de WhatsApp não pode estar ligado a dois canais (o webhook não saberia para onde ir).
        b.HasIndex(x => new { x.ChannelType, x.ExternalAccountId }).IsUnique().HasFilter("\"ExternalAccountId\" IS NOT NULL");
        b.HasIndex(x => new { x.CompanyId, x.ChannelType });
        b.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Title).HasMaxLength(160).IsRequired();
        b.Property(x => x.Message).HasMaxLength(1000);
        b.Property(x => x.Link).HasMaxLength(255);
        b.HasIndex(x => new { x.CompanyId, x.UserId, x.ReadAt });
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AIUsageRecordConfiguration : IEntityTypeConfiguration<AIUsageRecord>
{
    public void Configure(EntityTypeBuilder<AIUsageRecord> b)
    {
        b.ToTable("AIUsageRecords");
        b.Property(x => x.Provider).HasMaxLength(40);
        b.Property(x => x.Model).HasMaxLength(60);
        b.Property(x => x.ErrorCode).HasMaxLength(60);
        b.Property(x => x.EstimatedCostUsd).HasPrecision(12, 6);
        b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.CompanyId, x.CreatedAt });
        b.HasIndex(x => new { x.AgentId, x.CreatedAt });
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AIBillingEntryConfiguration : IEntityTypeConfiguration<AIBillingEntry>
{
    public void Configure(EntityTypeBuilder<AIBillingEntry> b)
    {
        b.ToTable("AIBillingEntries");
        b.Property(x => x.Provider).HasMaxLength(40).IsRequired();
        b.Property(x => x.AmountUsd).HasPrecision(12, 2);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasIndex(x => new { x.Month, x.Provider }).IsUnique();
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.Property(x => x.Action).HasMaxLength(80).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(80);
        b.Property(x => x.EntityId).HasMaxLength(64);
        b.Property(x => x.Details).HasMaxLength(4000);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.HasIndex(x => new { x.CompanyId, x.CreatedAt });
        b.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}
