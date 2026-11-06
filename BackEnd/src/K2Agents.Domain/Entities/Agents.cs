using K2Agents.Domain.Common;
using K2Agents.Domain.Enums;

namespace K2Agents.Domain.Entities;

/// <summary>Agente de IA configurado por uma empresa.</summary>
public class AIAgent : Entity, ITenantEntity
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public BusinessType BusinessType { get; set; } = BusinessType.GeneralInfo;
    public string SystemPrompt { get; set; } = string.Empty;
    public string? WelcomeMessage { get; set; }
    public string Model { get; set; } = "gpt-4o-mini";
    public decimal Temperature { get; set; } = 0.4m;
    public int MaxOutputTokens { get; set; } = 600;
    public AgentStatus Status { get; set; } = AgentStatus.Draft;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Configuração do assistente visual (objetivo, tom, regras...) em JSON; origem do SystemPrompt.</summary>
    public string BehaviorJson { get; set; } = "{}";

    /// <summary>Idioma em que o agente responde, independente do idioma da interface.</summary>
    public string ResponseLanguage { get; set; } = "pt-BR";
    public int CurrentVersion { get; set; } = 1;

    public ICollection<AgentVersion> Versions { get; set; } = new List<AgentVersion>();
}

/// <summary>Snapshot imutável da configuração do agente a cada alteração salva.</summary>
public class AgentVersion : Entity, ITenantEntity
{
    public Guid CompanyId { get; set; }
    public Guid AgentId { get; set; }
    public AIAgent? Agent { get; set; }
    public int VersionNumber { get; set; }
    public string SystemPrompt { get; set; } = string.Empty;
    public string BehaviorJson { get; set; } = "{}";
    public string Model { get; set; } = string.Empty;
    public decimal Temperature { get; set; }
    public int MaxOutputTokens { get; set; }
    public string? WelcomeMessage { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? ChangeNote { get; set; }
}

/// <summary>Documento da base de conhecimento. AgentId nulo = disponível a todos os agentes da empresa.</summary>
public class KnowledgeDocument : Entity, ITenantEntity
{
    public Guid CompanyId { get; set; }
    public Guid? AgentId { get; set; }
    public AIAgent? Agent { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public KnowledgeSourceType SourceType { get; set; } = KnowledgeSourceType.Text;
    public string? FilePath { get; set; }
    public string? OriginalFileName { get; set; }
    public long? FileSizeBytes { get; set; }
    public KnowledgeStatus Status { get; set; } = KnowledgeStatus.Ready;
    public string? ErrorMessage { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<KnowledgeChunk> Chunks { get; set; } = new List<KnowledgeChunk>();
}

/// <summary>
/// Trecho indexado de um documento — a unidade de busca e de envio ao modelo.
/// AgentId é copiado do documento para filtrar sem join. Futuramente receberá o embedding (pgvector).
/// </summary>
public class KnowledgeChunk : Entity, ITenantEntity
{
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public KnowledgeDocument? Document { get; set; }
    public Guid? AgentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
}
