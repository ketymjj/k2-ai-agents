using System.Text.RegularExpressions;
using K2Agents.Application.Abstractions;
using K2Agents.Domain.Enums;
using K2Agents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Infrastructure.Knowledge;

/// <summary>
/// Busca textual do PostgreSQL (tsvector + GIN, dicionário português, ranqueamento ts_rank_cd).
/// Em provedores que não são PostgreSQL usa correspondência de palavras-chave equivalente.
/// O filtro de empresa é sempre explícito (o SQL bruto não passa pelo filtro global).
/// </summary>
public partial class KnowledgeSearchService(AppDbContext db) : IKnowledgeSearchService
{
    private const int MaxTerms = 12;

    /// <summary>Palavras sem valor de busca (o dicionário do PostgreSQL já as ignora; aqui valem para o modo por palavras-chave).</summary>
    private static readonly HashSet<string> StopWords =
    [
        "que", "qual", "quais", "como", "para", "com", "uma", "umas", "uns", "tem", "têm", "ter", "vocês", "voces", "você", "voce",
        "dos", "das", "nos", "nas", "por", "mais", "muito", "sim", "não", "nao", "isso", "esse", "essa", "este", "esta", "aqui",
        "ser", "sua", "seu", "meu", "minha", "pode", "posso", "quero", "gostaria", "sobre", "onde", "quando", "quanto",
        "the", "and", "you", "your", "are", "for", "with", "does", "have", "can", "what", "how", "which", "there", "this", "that"
    ];

    public async Task<IReadOnlyList<KnowledgeSnippet>> SearchAsync(Guid companyId, Guid? agentId, string query, int maxResults, CancellationToken ct)
    {
        var terms = Terms(query);
        if (terms.Count == 0) return [];

        return db.Database.IsNpgsql()
            ? await FullTextAsync(companyId, agentId, terms, maxResults, ct)
            : await KeywordAsync(companyId, agentId, terms, maxResults, ct);
    }

    /// <summary>Palavras significativas da pergunta. Só letras e números: nada da sintaxe de busca chega ao banco.</summary>
    public static IReadOnlyList<string> Terms(string query) =>
        WordPattern().Matches(query.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .Distinct()
            .Take(MaxTerms)
            .ToList();

    private async Task<IReadOnlyList<KnowledgeSnippet>> FullTextAsync(
        Guid companyId, Guid? agentId, IReadOnlyList<string> terms, int maxResults, CancellationToken ct)
    {
        // Termos combinados com "or": qualquer palavra relevante encontra o trecho; o ranking ordena.
        var search = string.Join(" or ", terms);
        var ready = nameof(KnowledgeStatus.Ready);

        var hits = agentId.HasValue
            ? db.Database.SqlQuery<ChunkHit>($"""
                SELECT c."DocumentId", d."Title", c."Content", ts_rank_cd(c."SearchVector", q, 32)::float8 AS "Score"
                FROM "KnowledgeChunks" c
                JOIN "KnowledgeDocuments" d ON d."Id" = c."DocumentId",
                     websearch_to_tsquery('portuguese', immutable_unaccent({search})) q
                WHERE c."CompanyId" = {companyId} AND d."Status" = {ready}
                  AND (c."AgentId" IS NULL OR c."AgentId" = {agentId.Value})
                  AND c."SearchVector" @@ q
                ORDER BY "Score" DESC, c."ChunkIndex"
                LIMIT {maxResults}
                """)
            : db.Database.SqlQuery<ChunkHit>($"""
                SELECT c."DocumentId", d."Title", c."Content", ts_rank_cd(c."SearchVector", q, 32)::float8 AS "Score"
                FROM "KnowledgeChunks" c
                JOIN "KnowledgeDocuments" d ON d."Id" = c."DocumentId",
                     websearch_to_tsquery('portuguese', immutable_unaccent({search})) q
                WHERE c."CompanyId" = {companyId} AND d."Status" = {ready}
                  AND c."SearchVector" @@ q
                ORDER BY "Score" DESC, c."ChunkIndex"
                LIMIT {maxResults}
                """);

        var list = await hits.ToListAsync(ct);
        return list.Select(h => new KnowledgeSnippet(h.DocumentId, h.Title, h.Content, h.Score)).ToList();
    }

    private async Task<IReadOnlyList<KnowledgeSnippet>> KeywordAsync(
        Guid companyId, Guid? agentId, IReadOnlyList<string> terms, int maxResults, CancellationToken ct)
    {
        var chunks = await db.KnowledgeChunks.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Document!.Status == KnowledgeStatus.Ready
                        && (!agentId.HasValue || c.AgentId == null || c.AgentId == agentId))
            .Select(c => new { c.DocumentId, c.Document!.Title, c.Content, c.ChunkIndex })
            .Take(2000)
            .ToListAsync(ct);

        // Termos mais longos são mais específicos e pesam mais; o prefixo aproxima variações (encomenda/encomendas).
        // Sem acentos dos dois lados, como na busca do PostgreSQL.
        var plainTerms = terms.Select(RemoveDiacritics).ToList();
        var totalWeight = plainTerms.Sum(t => t.Length);
        return chunks
            .Select(c =>
            {
                var text = RemoveDiacritics((c.Title + " " + c.Content).ToLowerInvariant());
                var matched = plainTerms.Where(t => text.Contains(t[..Math.Min(t.Length, 6)])).Sum(t => t.Length);
                return (c, score: matched / (double)totalWeight);
            })
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score).ThenBy(x => x.c.ChunkIndex)
            .Take(maxResults)
            .Select(x => new KnowledgeSnippet(x.c.DocumentId, x.c.Title, x.c.Content, x.score))
            .ToList();
    }

    private static string RemoveDiacritics(string text)
    {
        var decomposed = text.Normalize(System.Text.NormalizationForm.FormD);
        var chars = decomposed.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(chars.ToArray()).Normalize(System.Text.NormalizationForm.FormC);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();

    private sealed class ChunkHit
    {
        public Guid DocumentId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public double Score { get; set; }
    }
}
