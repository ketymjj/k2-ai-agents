using System.Text;
using System.Text.RegularExpressions;

namespace K2Agents.Application.Knowledge;

/// <summary>
/// Divide textos em trechos de tamanho controlado, respeitando parágrafos e frases,
/// com sobreposição para não perder contexto entre trechos vizinhos.
/// </summary>
public static partial class KnowledgeChunker
{
    public const int TargetSize = 900;
    public const int Overlap = 150;

    public static IReadOnlyList<string> Split(string text, int targetSize = TargetSize, int overlap = Overlap)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0) return [];
        if (normalized.Length <= targetSize) return [normalized];

        // Unidades: parágrafos; parágrafos longos viram frases; frases longas são cortadas.
        var units = new List<string>();
        foreach (var paragraph in normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (paragraph.Length <= targetSize) units.Add(paragraph);
            else foreach (var sentence in SentenceBoundary().Split(paragraph))
                units.AddRange(HardSplit(sentence.Trim(), targetSize));
        }

        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var unit in units.Where(u => u.Length > 0))
        {
            if (current.Length > 0 && current.Length + unit.Length + 2 > targetSize)
            {
                var chunk = current.ToString().Trim();
                chunks.Add(chunk);
                current.Clear();
                current.Append(Tail(chunk, overlap));
            }
            if (current.Length > 0) current.Append("\n\n");
            current.Append(unit);
        }
        // Após cada corte sempre entra uma unidade nova, então o restante nunca é só sobreposição.
        if (current.Length > 0) chunks.Add(current.ToString().Trim());
        return chunks;
    }

    /// <summary>Unifica quebras de linha e remove espaços redundantes e caracteres de controle.</summary>
    public static string Normalize(string text)
    {
        var cleaned = text.Replace("\r\n", "\n").Replace('\r', '\n');
        cleaned = ControlChars().Replace(cleaned, " ");
        cleaned = MultiSpace().Replace(cleaned, " ");
        cleaned = MultiBreak().Replace(cleaned, "\n\n");
        return string.Join('\n', cleaned.Split('\n').Select(l => l.Trim())).Trim();
    }

    private static IEnumerable<string> HardSplit(string text, int size)
    {
        for (var i = 0; i < text.Length; i += size)
            yield return text.Substring(i, Math.Min(size, text.Length - i));
    }

    /// <summary>Final do trecho anterior, começando numa palavra inteira.</summary>
    private static string Tail(string chunk, int overlap)
    {
        if (chunk.Length <= overlap) return chunk;
        var tail = chunk[^overlap..];
        var space = tail.IndexOf(' ');
        return space > 0 ? tail[(space + 1)..] : tail;
    }

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBoundary();

    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F]")]
    private static partial Regex ControlChars();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex MultiSpace();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultiBreak();
}
