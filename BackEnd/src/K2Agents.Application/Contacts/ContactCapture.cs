using System.Text.RegularExpressions;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Automations;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Application.Contacts;

public record CapturedData(string? Name, string? Email, string? Phone)
{
    public bool IsEmpty => Name is null && Email is null && Phone is null;
}

/// <summary>
/// Extrai dados pessoais SOMENTE do texto escrito pelo próprio cliente, com padrões determinísticos.
/// Nada é inferido pela IA — o agente não pode "inventar" dados de contato.
/// </summary>
public static partial class ContactExtractor
{
    public static CapturedData Extract(string message)
    {
        var email = EmailPattern().Match(message) is { Success: true } e ? e.Value.ToLowerInvariant() : null;

        string? phone = null;
        foreach (Match m in PhonePattern().Matches(message))
        {
            var digits = new string(m.Value.Where(char.IsDigit).ToArray());
            if (digits.Length is >= 10 and <= 13) { phone = digits; break; }
        }

        string? name = null;
        if (NamePattern().Match(message) is { Success: true } n)
        {
            var candidate = n.Groups["name"].Value.Trim();
            if (candidate.Length is >= 2 and <= 80) name = candidate;
        }
        return new CapturedData(name, email, phone);
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailPattern();

    /// <summary>Telefones brasileiros: (11) 98888-1234, 11988881234, +55 11 98888-1234.</summary>
    [GeneratedRegex(@"(?:\+?55[\s\-]?)?\(?\d{2}\)?[\s\-]?9?\d{4}[\s\-]?\d{4}")]
    private static partial Regex PhonePattern();

    /// <summary>Nome em frases explícitas ("meu nome é Ana Souza", "me chamo...", "my name is..."), palavras iniciadas em maiúscula.</summary>
    // Só o prefixo é insensível a maiúsculas: o nome precisa começar com maiúscula (evita "sou a cliente...").
    [GeneratedRegex(@"(?i:meu nome (?:é|e)|me chamo|aqui (?:é|e) (?:o|a)|my name is|this is)\s+(?<name>\p{Lu}[\p{L}'’\-]+(?:\s+(?:d[aeo]s?\s+)?\p{Lu}[\p{L}'’\-]+){0,3})",
        RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}

public interface IContactCaptureService
{
    /// <summary>Atualiza/cria o contato da conversa a partir da mensagem do cliente. Retorna true se mudou algo.</summary>
    Task<bool> CaptureAsync(Conversation conversation, string customerMessage, CancellationToken ct);
}

public class ContactCaptureService(IAppDbContext db, IAuditService audit, IAutomationEngine automations) : IContactCaptureService
{
    public async Task<bool> CaptureAsync(Conversation conversation, string customerMessage, CancellationToken ct)
    {
        var data = ContactExtractor.Extract(customerMessage);
        if (data.IsEmpty) return false;

        Contact? contact = conversation.ContactId is { } id ? await db.Contacts.FirstOrDefaultAsync(c => c.Id == id, ct) : null;
        if (contact?.IsAnonymized == true) return false;

        // Sem contato vinculado: reaproveita um existente da empresa com o mesmo e-mail/telefone (nunca duplica).
        if (contact is null && (data.Email is not null || data.Phone is not null))
        {
            contact = await db.Contacts.FirstOrDefaultAsync(c => c.CompanyId == conversation.CompanyId && !c.IsAnonymized
                && ((data.Email != null && c.Email == data.Email) || (data.Phone != null && c.Phone == data.Phone)), ct);
            if (contact is not null) conversation.ContactId = contact.Id;
        }

        var isNew = false;
        if (contact is null)
        {
            // Só nome não basta para criar um lead: é preciso um meio de contato.
            if (data.Email is null && data.Phone is null) return false;
            contact = new Contact
            {
                CompanyId = conversation.CompanyId,
                Name = data.Name ?? $"Visitante {conversation.Id.ToString()[..4].ToUpperInvariant()}",
                Source = conversation.Channel.ToString(),
                Status = ContactStatus.New,
                IsLead = true
            };
            db.Contacts.Add(contact);
            conversation.ContactId = contact.Id;
            isNew = true;
        }

        // Completa apenas campos vazios: não sobrescreve dados confirmados pela equipe.
        var changed = isNew;
        if (data.Email is not null && contact.Email is null) { contact.Email = data.Email; changed = true; }
        if (data.Phone is not null && contact.Phone is null) { contact.Phone = data.Phone; changed = true; }
        if (data.Name is not null && contact.Name.StartsWith("Visitante ")) { contact.Name = data.Name; changed = true; }
        if (!contact.IsLead && (contact.Email is not null || contact.Phone is not null)) { contact.IsLead = true; changed = true; }
        if (!changed) return false;

        contact.UpdatedAt = DateTime.UtcNow;
        audit.Record(isNew ? "contact.captured" : "contact.enriched", nameof(Contact), contact.Id.ToString(),
            $"conversa {conversation.Id}", conversation.CompanyId);
        await db.SaveChangesAsync(ct);

        if (isNew)
            await automations.PublishAsync(new AutomationEvent(conversation.CompanyId, AutomationTrigger.LeadCreated,
                new Dictionary<string, string> { ["contato"] = contact.Name, ["canal"] = conversation.Channel.ToString() },
                $"/contacts/{contact.Id}"), ct);
        return true;
    }
}
