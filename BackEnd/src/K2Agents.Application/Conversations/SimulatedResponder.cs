using System.Globalization;
using System.Text;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Agents;
using K2Agents.Domain.Entities;

namespace K2Agents.Application.Conversations;

public record SimulatedReply(string Content, Guid? SourceDocumentId = null);

public interface ISimulatedResponder
{
    SimulatedReply Reply(AIAgent agent, string message, IReadOnlyList<KnowledgeSnippet> snippets);
}

/// <summary>
/// Modo demonstrativo: respostas determinísticas a partir das perguntas frequentes do agente e do
/// trecho mais relevante da base de conhecimento. NÃO usa IA — toda resposta é marcada como simulada
/// na API, no banco e na interface.
/// </summary>
public class SimulatedResponder : ISimulatedResponder
{
    private const int MaxExcerptChars = 600;

    private static readonly string[] Greetings = ["oi", "ola", "bom dia", "boa tarde", "boa noite", "hello", "hi", "hey", "good morning"];
    private static readonly HashSet<string> StopWords =
    [
        "que", "qual", "quais", "como", "para", "com", "uma", "voces", "vocês", "tem", "esse", "essa", "isso", "por", "dos", "das",
        "the", "what", "which", "how", "you", "your", "are", "for", "with", "does", "have", "can"
    ];

    public SimulatedReply Reply(AIAgent agent, string message, IReadOnlyList<KnowledgeSnippet> snippets)
    {
        var en = agent.ResponseLanguage == "en";
        var normalized = Normalize(message);

        if (HandoffDetector.CustomerAsksForHuman(message))
            return new(AgentOrchestrator.HandoffNotice(agent.ResponseLanguage));

        var behavior = AgentService.ParseBehavior(agent.BehaviorJson);
        var best = behavior.Faqs
            .Select(f => (faq: f, score: Similarity(normalized, Normalize(f.Question))))
            .OrderByDescending(x => x.score)
            .FirstOrDefault();
        if (best.faq is not null && best.score >= 0.5) return new(best.faq.Answer);

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 4 && Greetings.Any(g => normalized.StartsWith(g)))
            return new(agent.WelcomeMessage ?? (en ? "Hi! How can I help you?" : "Olá! Como posso ajudar?"));

        if (snippets.Count > 0)
        {
            var top = snippets[0];
            var excerpt = Excerpt(top.Content);
            return new(en
                ? $"According to the knowledge base ({top.Title}):\n{excerpt}"
                : $"Segundo a base de conhecimento ({top.Title}):\n{excerpt}", top.DocumentId);
        }

        return new(en
            ? "In demo mode I can only answer from this agent's FAQs and knowledge base, and I found nothing about that. Configure an API key to get AI-generated answers."
            : "No modo demonstrativo eu respondo apenas com as perguntas frequentes e a base de conhecimento deste agente, e não encontrei nada sobre isso. Configure uma chave de API para obter respostas geradas por IA.");
    }

    /// <summary>Início do trecho, cortado no fim de uma frase quando possível.</summary>
    private static string Excerpt(string content)
    {
        var text = content.Trim();
        if (text.Length <= MaxExcerptChars) return text;
        var cut = text[..MaxExcerptChars];
        var end = cut.LastIndexOfAny(['.', '!', '?', '\n']);
        return (end > MaxExcerptChars / 2 ? cut[..(end + 1)] : cut + "…").Trim();
    }

    /// <summary>Fração das palavras relevantes da pergunta cadastrada presentes na mensagem.</summary>
    private static double Similarity(string message, string question)
    {
        var questionWords = Keywords(question);
        if (questionWords.Count == 0) return 0;
        var messageWords = Keywords(message);
        return questionWords.Count(messageWords.Contains) / (double)questionWords.Count;
    }

    private static HashSet<string> Keywords(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3 && !StopWords.Contains(w)).ToHashSet();

    private static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
