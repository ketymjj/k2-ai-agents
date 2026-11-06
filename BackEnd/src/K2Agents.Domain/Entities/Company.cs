using K2Agents.Domain.Common;
using K2Agents.Domain.Enums;

namespace K2Agents.Domain.Entities;

/// <summary>Empresa cliente da plataforma (tenant).</summary>
public class Company : Entity
{
    public string Name { get; set; } = string.Empty;
    public string? Document { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Industry { get; set; }
    public string? Website { get; set; }
    public CompanyStatus Status { get; set; } = CompanyStatus.Active;

    /// <summary>Dias de retenção das conversas (LGPD). Nulo = sem expurgo automático.</summary>
    public int? ConversationRetentionDays { get; set; }

    /// <summary>Serviços agendáveis, horário de atendimento e regras de agenda (JSON).</summary>
    public string SchedulingJson { get; set; } = "{}";

    public ICollection<AIAgent> Agents { get; set; } = new List<AIAgent>();
    public ICollection<User> Users { get; set; } = new List<User>();
}
