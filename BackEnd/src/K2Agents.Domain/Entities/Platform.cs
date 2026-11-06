using K2Agents.Domain.Common;
using K2Agents.Domain.Enums;

namespace K2Agents.Domain.Entities;

/// <summary>Regra de automação: gatilho e ação pertencem a conjuntos fechados (nenhum código arbitrário).</summary>
public class AutomationRule : Entity, ITenantEntity
{
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public AutomationTrigger TriggerType { get; set; }
    public AutomationAction ActionType { get; set; }

    /// <summary>Parâmetros da ação em JSON, validados contra o esquema da ação antes de salvar.</summary>
    public string Configuration { get; set; } = "{}";
    public bool Enabled { get; set; } = true;
    public int ExecutionCount { get; set; }
    public DateTime? LastExecutedAt { get; set; }
}

/// <summary>Canal de atendimento (Web Chat, WhatsApp) vinculado a um agente.</summary>
public class ChannelConfiguration : Entity, ITenantEntity
{
    public Guid CompanyId { get; set; }
    public Guid AgentId { get; set; }
    public AIAgent? Agent { get; set; }
    public ChannelType ChannelType { get; set; }
    public string Name { get; set; } = string.Empty;
    public ChannelStatus Status { get; set; } = ChannelStatus.Draft;

    /// <summary>Token público do widget: apenas identifica o canal, não concede acesso a dados.</summary>
    public string? PublicToken { get; set; }

    /// <summary>Configuração visual e comportamental não sensível (cores, título, mensagem inicial).</summary>
    public string SettingsJson { get; set; } = "{}";

    /// <summary>Identificador da conta no provedor externo (WhatsApp: Phone Number ID), usado para rotear webhooks.</summary>
    public string? ExternalAccountId { get; set; }

    /// <summary>Credenciais do canal criptografadas (ex.: token do WhatsApp). Nunca retornadas pela API.</summary>
    public string? EncryptedSecrets { get; set; }

    /// <summary>Domínios autorizados a incorporar o widget (separados por vírgula).</summary>
    public string? AllowedOrigins { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Notification : Entity, ITenantEntity
{
    public Guid CompanyId { get; set; }

    /// <summary>Destinatário específico; nulo = todos os usuários da empresa.</summary>
    public Guid? UserId { get; set; }
    public NotificationType Type { get; set; } = NotificationType.Info;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Link { get; set; }
    public DateTime? ReadAt { get; set; }
}

/// <summary>Consumo de IA por chamada. Custo é ESTIMADO pela tabela de preços configurada, não faturado.</summary>
public class AIUsageRecord : Entity, ITenantEntity
{
    public Guid CompanyId { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? ConversationId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public int LatencyMs { get; set; }
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public bool IsSimulated { get; set; }
    public ChannelType Channel { get; set; }
}

/// <summary>
/// Valor efetivamente faturado pelo provedor de IA num mês (lançado pelo SuperAdmin a partir da fatura),
/// usado para conciliar com o custo estimado. É da plataforma: a conta do provedor é única para todas as empresas.
/// </summary>
public class AIBillingEntry : Entity
{
    /// <summary>Primeiro dia do mês de referência.</summary>
    public DateOnly Month { get; set; }
    public string Provider { get; set; } = "OpenAI";
    public decimal AmountUsd { get; set; }
    public string? Notes { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Trilha de auditoria. CompanyId nulo = ação de plataforma (SuperAdmin).</summary>
public class AuditLog : Entity
{
    public Guid? CompanyId { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
}
