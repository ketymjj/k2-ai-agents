using K2Agents.Domain.Common;
using K2Agents.Domain.Enums;

namespace K2Agents.Domain.Entities;

public class Conversation : Entity, ITenantEntity
{
    public Guid CompanyId { get; set; }
    public Guid AgentId { get; set; }
    public AIAgent? Agent { get; set; }
    public Guid? ContactId { get; set; }
    public Contact? Contact { get; set; }
    public ChannelType Channel { get; set; } = ChannelType.WebChat;
    public ConversationStatus Status { get; set; } = ConversationStatus.Open;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    /// <summary>Atendente humano que assumiu a conversa (handoff).</summary>
    public Guid? AssignedUserId { get; set; }
    public User? AssignedUser { get; set; }

    /// <summary>Identificador externo da sessão (visitante do Web Chat, wa_id do WhatsApp).</summary>
    public string? ExternalSessionId { get; set; }

    /// <summary>Marca conversas geradas pelo seed de demonstração.</summary>
    public bool IsDemo { get; set; }

    public ICollection<ConversationMessage> Messages { get; set; } = new List<ConversationMessage>();
}

public class ConversationMessage : Entity, ITenantEntity
{
    public Guid CompanyId { get; set; }
    public Guid ConversationId { get; set; }
    public Conversation? Conversation { get; set; }
    public SenderType SenderType { get; set; }
    public Guid? SenderUserId { get; set; }
    public string Content { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }

    /// <summary>Tempo de geração da resposta da IA, em milissegundos.</summary>
    public int? ResponseTimeMs { get; set; }

    /// <summary>Resposta produzida pelo modo demonstrativo (não é IA real).</summary>
    public bool IsSimulated { get; set; }

    /// <summary>Id externo da mensagem no canal (idempotência de webhooks).</summary>
    public string? ExternalMessageId { get; set; }

    /// <summary>Documentos da base de conhecimento usados na resposta (JSON: [{documentId,title}]).</summary>
    public string? KnowledgeSources { get; set; }
}

public class Contact : Entity, ITenantEntity
{
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Source { get; set; }
    public ContactStatus Status { get; set; } = ContactStatus.New;
    public bool IsLead { get; set; }
    public InterestLevel Interest { get; set; } = InterestLevel.Unknown;
    public string? Notes { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsAnonymized { get; set; }
}

public class Appointment : Entity, ITenantEntity
{
    public Guid CompanyId { get; set; }
    public Guid? ContactId { get; set; }
    public Contact? Contact { get; set; }
    public Guid? AgentId { get; set; }
    public AIAgent? Agent { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;
    public string? Notes { get; set; }

    /// <summary>Quem criou: "Manual" (equipe) ou "Agent" (agente de IA, após consultar a disponibilidade).</summary>
    public string Source { get; set; } = "Manual";
    public Guid? ConversationId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
