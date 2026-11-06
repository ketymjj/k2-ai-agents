using K2Agents.Domain.Enums;

namespace K2Agents.Application.Abstractions;

public record KnowledgeSnippet(Guid DocumentId, string Title, string Content, double Score);

/// <summary>
/// Busca de trechos relevantes. Implementação atual: busca textual do PostgreSQL.
/// Uma implementação vetorial (pgvector) pode substituí-la sem mudar quem consome.
/// </summary>
public interface IKnowledgeSearchService
{
    /// <summary>Sempre filtrado pela empresa informada; AgentId restringe a documentos do agente ou gerais.</summary>
    Task<IReadOnlyList<KnowledgeSnippet>> SearchAsync(Guid companyId, Guid? agentId, string query, int maxResults, CancellationToken ct);
}

public record ExtractedText(string Text, bool Truncated);

/// <summary>Extrai texto de arquivos enviados, validando o conteúdo real (não só a extensão).</summary>
public interface ITextExtractor
{
    /// <summary>Lança <see cref="Common.AppException"/> com mensagem amigável se o arquivo for inválido.</summary>
    Task<ExtractedText> ExtractAsync(Stream content, KnowledgeSourceType type, CancellationToken ct);
}

/// <summary>Armazenamento de arquivos originais. Caminhos sempre gerados pelo sistema, nunca pelo usuário.</summary>
public interface IFileStorage
{
    Task<string> SaveAsync(Guid companyId, string extension, Stream content, CancellationToken ct);
    Stream? OpenRead(string relativePath);
    void Delete(string relativePath);
}
