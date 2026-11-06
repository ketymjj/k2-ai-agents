using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace K2Agents.Application.Conversations;

/// <summary>
/// Sinais de transferência para atendimento humano:
/// (1) o cliente pede explicitamente uma pessoa — frases, não palavras soltas ("bolo para 20 pessoas" não conta);
/// (2) o modelo inclui a marcação de transferência, ensinada pelas regras da plataforma.
/// </summary>
public static partial class HandoffDetector
{
    public const string MarkerPt = "[[TRANSFERIR]]";
    public const string MarkerEn = "[[HANDOFF]]";

    public static bool CustomerAsksForHuman(string message)
    {
        var text = Normalize(message);
        return CustomerRequestPattern().IsMatch(text);
    }

    /// <summary>Remove a marcação da resposta do modelo; indica se ela estava presente.</summary>
    public static (string Content, bool Requested) ExtractMarker(string reply)
    {
        var requested = reply.Contains(MarkerPt, StringComparison.OrdinalIgnoreCase) || reply.Contains(MarkerEn, StringComparison.OrdinalIgnoreCase);
        if (!requested) return (reply, false);
        var cleaned = reply.Replace(MarkerPt, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(MarkerEn, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        return (cleaned, true);
    }

    private static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    [GeneratedRegex(
        @"\b(falar|conversar|ser atendid[oa]) (com )?(um |uma |algum |alguma )?(atendente|humano|pessoa|alguem|funcionari[oa]|gerente|responsavel)\b" +
        @"|\b(quero|preciso de|gostaria de|me passa para|me transfere para|transfere para) (um |uma |o |a )?(atendente|humano|pessoa de verdade|gerente)\b" +
        @"|\batendimento humano\b|\batendente humano\b|\bpessoa de verdade\b|\bpessoa real\b" +
        @"|\b(talk|speak|chat) (to|with) (a |an |the )?(human|person|real person|agent|attendant|representative|manager)\b" +
        @"|\b(human agent|real person|live agent)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex CustomerRequestPattern();
}
