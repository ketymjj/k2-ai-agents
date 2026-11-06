using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;

namespace K2Agents.Application.Knowledge;

public static class KnowledgeIndexer
{
    /// <summary>Gera os trechos pesquisáveis do documento. Uma pergunta frequente vira um único trecho pergunta + resposta.</summary>
    public static IEnumerable<KnowledgeChunk> BuildChunks(KnowledgeDocument doc)
    {
        var parts = doc.SourceType == KnowledgeSourceType.Faq
            ? [$"Pergunta: {doc.Title}\nResposta: {doc.Content}"]
            : KnowledgeChunker.Split(doc.Content);

        var index = 0;
        foreach (var part in parts)
        {
            yield return new KnowledgeChunk
            {
                CompanyId = doc.CompanyId,
                DocumentId = doc.Id,
                AgentId = doc.AgentId,
                ChunkIndex = index++,
                Content = part
            };
        }
    }
}
