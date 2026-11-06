using FluentValidation;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Common;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Application.Knowledge;

public interface IKnowledgeService
{
    Task<PagedResult<KnowledgeListItemDto>> ListAsync(KnowledgeQuery query, CancellationToken ct);
    Task<KnowledgeDocumentDto> GetAsync(Guid id, CancellationToken ct);
    Task<KnowledgeDocumentDto> CreateAsync(CreateKnowledgeRequest request, CancellationToken ct);
    Task<KnowledgeDocumentDto> UploadAsync(UploadKnowledgeRequest request, CancellationToken ct);
    Task<KnowledgeDocumentDto> UpdateAsync(Guid id, UpdateKnowledgeRequest request, CancellationToken ct);
    Task<KnowledgeDocumentDto> SetStatusAsync(Guid id, KnowledgeStatus status, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<(Stream Content, string FileName)> DownloadAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(SearchKnowledgeRequest request, CancellationToken ct);
}

/// <summary>
/// KnowledgeDocument e KnowledgeChunk são ITenantEntity: o filtro global isola as empresas.
/// O conteúdo dos documentos é tratado como dado não confiável — nunca como instrução.
/// </summary>
public class KnowledgeService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IAuditService audit,
    ITextExtractor extractor,
    IFileStorage storage,
    IKnowledgeSearchService search,
    KnowledgeSettings settings,
    IValidator<CreateKnowledgeRequest> createValidator,
    IValidator<UpdateKnowledgeRequest> updateValidator,
    IValidator<SearchKnowledgeRequest> searchValidator) : IKnowledgeService
{
    private static readonly Dictionary<string, KnowledgeSourceType> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = KnowledgeSourceType.Pdf,
        [".txt"] = KnowledgeSourceType.Txt,
        [".docx"] = KnowledgeSourceType.Docx
    };

    public async Task<PagedResult<KnowledgeListItemDto>> ListAsync(KnowledgeQuery query, CancellationToken ct)
    {
        var docs = db.KnowledgeDocuments.AsNoTracking();
        if (query.CompanyId.HasValue) docs = docs.Where(d => d.CompanyId == query.CompanyId);
        if (query.GeneralOnly == true) docs = docs.Where(d => d.AgentId == null);
        else if (query.AgentId.HasValue) docs = docs.Where(d => d.AgentId == query.AgentId);
        if (query.SourceType.HasValue) docs = docs.Where(d => d.SourceType == query.SourceType);
        if (query.Status.HasValue) docs = docs.Where(d => d.Status == query.Status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            docs = docs.Where(d => d.Title.ToLower().Contains(term));
        }

        return await docs
            .OrderByDescending(d => d.UpdatedAt)
            .Select(d => new KnowledgeListItemDto(
                d.Id, d.CompanyId,
                db.Companies.Where(c => c.Id == d.CompanyId).Select(c => c.Name).FirstOrDefault(),
                d.AgentId, d.Agent != null ? d.Agent.Name : null, d.Title, d.SourceType, d.Status,
                d.OriginalFileName, d.FileSizeBytes, d.Chunks.Count, d.Content.Length, d.CreatedAt, d.UpdatedAt))
            .ToPagedAsync(query, ct);
    }

    public async Task<KnowledgeDocumentDto> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await db.KnowledgeDocuments.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new KnowledgeDocumentDto(
                d.Id, d.CompanyId,
                db.Companies.Where(c => c.Id == d.CompanyId).Select(c => c.Name).FirstOrDefault(),
                d.AgentId, d.Agent != null ? d.Agent.Name : null, d.Title, d.Content, d.SourceType, d.Status,
                d.OriginalFileName, d.FileSizeBytes, d.FilePath != null, d.Chunks.Count, d.ErrorMessage, d.CreatedAt, d.UpdatedAt))
            .FirstOrDefaultAsync(ct);
        return dto ?? throw AppException.NotFound("Documento");
    }

    public async Task<KnowledgeDocumentDto> CreateAsync(CreateKnowledgeRequest request, CancellationToken ct)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var (companyId, agentId) = await ResolveScopeAsync(request.CompanyId, request.AgentId, ct);

        var doc = new KnowledgeDocument
        {
            CompanyId = companyId,
            AgentId = agentId,
            Title = request.Title.Trim(),
            Content = KnowledgeChunker.Normalize(request.Content),
            SourceType = request.SourceType,
            Status = KnowledgeStatus.Ready
        };
        db.KnowledgeDocuments.Add(doc);
        Index(doc);
        audit.Record("knowledge.created", nameof(KnowledgeDocument), doc.Id.ToString(), doc.Title, companyId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(doc.Id, ct);
    }

    public async Task<KnowledgeDocumentDto> UploadAsync(UploadKnowledgeRequest request, CancellationToken ct)
    {
        var extension = Path.GetExtension(request.FileName ?? string.Empty);
        if (!Extensions.TryGetValue(extension, out var type))
            throw new AppException("Formato não suportado. Envie arquivos PDF, TXT ou DOCX.", 400, "unsupported_file_type");
        if (request.Length <= 0) throw new AppException("O arquivo está vazio.", 400, "empty_file");
        if (request.Length > settings.MaxFileSizeBytes)
            throw new AppException($"O arquivo excede o limite de {settings.MaxFileSizeBytes / (1024 * 1024)} MB.", 400, "file_too_large");

        var (companyId, agentId) = await ResolveScopeAsync(request.CompanyId, request.AgentId, ct);

        // Cópia limitada em memória: usada para validar, extrair e armazenar sem confiar no tamanho declarado.
        using var buffer = new MemoryStream();
        await CopyLimitedAsync(request.Content, buffer, settings.MaxFileSizeBytes, ct);
        buffer.Position = 0;

        var extracted = await extractor.ExtractAsync(buffer, type, ct);
        var text = KnowledgeChunker.Normalize(extracted.Text);
        if (text.Length == 0)
            throw new AppException(type == KnowledgeSourceType.Pdf
                ? "Nenhum texto foi encontrado no PDF. PDFs digitalizados (apenas imagem) não são suportados."
                : "Nenhum texto foi encontrado no arquivo.", 400, "no_text_found");

        buffer.Position = 0;
        var path = await storage.SaveAsync(companyId, extension.ToLowerInvariant(), buffer, ct);

        var fileName = Path.GetFileName(request.FileName)!;
        var title = string.IsNullOrWhiteSpace(request.Title) ? Path.GetFileNameWithoutExtension(fileName) : request.Title.Trim();
        var doc = new KnowledgeDocument
        {
            CompanyId = companyId,
            AgentId = agentId,
            Title = title.Length > 200 ? title[..200] : title,
            Content = text,
            SourceType = type,
            FilePath = path,
            OriginalFileName = fileName.Length > 255 ? fileName[^255..] : fileName,
            FileSizeBytes = buffer.Length,
            Status = KnowledgeStatus.Ready,
            ErrorMessage = extracted.Truncated
                ? $"O texto foi limitado aos primeiros {settings.MaxTextChars:N0} caracteres."
                : null
        };
        db.KnowledgeDocuments.Add(doc);
        Index(doc);
        audit.Record("knowledge.uploaded", nameof(KnowledgeDocument), doc.Id.ToString(), $"{doc.OriginalFileName} ({doc.FileSizeBytes} bytes)", companyId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(doc.Id, ct);
    }

    public async Task<KnowledgeDocumentDto> UpdateAsync(Guid id, UpdateKnowledgeRequest request, CancellationToken ct)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var doc = await db.KnowledgeDocuments.Include(d => d.Chunks).FirstOrDefaultAsync(d => d.Id == id, ct)
                  ?? throw AppException.NotFound("Documento");
        var (_, agentId) = await ResolveScopeAsync(doc.CompanyId, request.AgentId, ct);

        doc.Title = request.Title.Trim();
        doc.AgentId = agentId;
        doc.Content = KnowledgeChunker.Normalize(request.Content);
        doc.UpdatedAt = DateTime.UtcNow;
        db.KnowledgeChunks.RemoveRange(doc.Chunks);
        doc.Chunks.Clear();
        Index(doc);
        audit.Record("knowledge.updated", nameof(KnowledgeDocument), doc.Id.ToString(), doc.Title, doc.CompanyId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<KnowledgeDocumentDto> SetStatusAsync(Guid id, KnowledgeStatus status, CancellationToken ct)
    {
        if (status is not (KnowledgeStatus.Ready or KnowledgeStatus.Archived))
            throw new AppException("Status inválido.", 400, "invalid_status");
        var doc = await db.KnowledgeDocuments.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw AppException.NotFound("Documento");
        if (doc.Status != status)
        {
            doc.Status = status;
            doc.UpdatedAt = DateTime.UtcNow;
            audit.Record(status == KnowledgeStatus.Archived ? "knowledge.archived" : "knowledge.restored",
                nameof(KnowledgeDocument), doc.Id.ToString(), doc.Title, doc.CompanyId);
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        // Trechos carregados para exclusão explícita, sem depender do cascade do banco.
        var doc = await db.KnowledgeDocuments.Include(d => d.Chunks).FirstOrDefaultAsync(d => d.Id == id, ct)
                  ?? throw AppException.NotFound("Documento");
        var path = doc.FilePath;
        db.KnowledgeChunks.RemoveRange(doc.Chunks);
        db.KnowledgeDocuments.Remove(doc);
        audit.Record("knowledge.deleted", nameof(KnowledgeDocument), doc.Id.ToString(), doc.Title, doc.CompanyId);
        await db.SaveChangesAsync(ct);
        if (path is not null) storage.Delete(path);
    }

    public async Task<(Stream Content, string FileName)> DownloadAsync(Guid id, CancellationToken ct)
    {
        var doc = await db.KnowledgeDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw AppException.NotFound("Documento");
        if (doc.FilePath is null) throw new AppException("Este documento não possui arquivo original.", 404, "no_file");
        var stream = storage.OpenRead(doc.FilePath) ?? throw new AppException("O arquivo original não foi encontrado.", 404, "file_missing");
        return (stream, doc.OriginalFileName ?? $"documento{Path.GetExtension(doc.FilePath)}");
    }

    public async Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsync(SearchKnowledgeRequest request, CancellationToken ct)
    {
        await searchValidator.ValidateAndThrowAsync(request, ct);
        var (companyId, agentId) = await ResolveScopeAsync(request.CompanyId, request.AgentId, ct, requireActiveCompany: false);
        var snippets = await search.SearchAsync(companyId, agentId, request.Query, Math.Max(settings.MaxSnippets, 5), ct);
        return snippets.Select(s => new KnowledgeSearchHit(s.DocumentId, s.Title, s.Content, Math.Round(s.Score, 4))).ToList();
    }

    private void Index(KnowledgeDocument doc)
    {
        foreach (var chunk in KnowledgeIndexer.BuildChunks(doc))
        {
            doc.Chunks.Add(chunk);
            db.KnowledgeChunks.Add(chunk);
        }
    }

    /// <summary>
    /// Empresa do documento: a do usuário; para o Super Admin, a informada ou a do agente.
    /// O agente, quando informado, precisa pertencer à mesma empresa.
    /// </summary>
    private async Task<(Guid CompanyId, Guid? AgentId)> ResolveScopeAsync(
        Guid? requestedCompany, Guid? agentId, CancellationToken ct, bool requireActiveCompany = true)
    {
        AIAgent? agent = null;
        if (agentId.HasValue)
            agent = await db.AIAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, ct) ?? throw AppException.NotFound("Agente");

        Guid companyId;
        if (currentUser.IsSuperAdmin)
            companyId = requestedCompany ?? agent?.CompanyId ?? throw new AppException("Informe a empresa.", 400, "company_required");
        else
        {
            if (requestedCompany.HasValue && requestedCompany != currentUser.CompanyId) throw AppException.Forbidden();
            companyId = currentUser.CompanyId ?? throw AppException.Forbidden();
        }

        if (agent is not null && agent.CompanyId != companyId)
            throw new AppException("O agente pertence a outra empresa.", 400, "agent_company_mismatch");

        if (requireActiveCompany)
        {
            var status = await db.Companies.Where(c => c.Id == companyId).Select(c => (CompanyStatus?)c.Status).FirstOrDefaultAsync(ct)
                         ?? throw AppException.NotFound("Empresa");
            if (status != CompanyStatus.Active) throw new AppException("A empresa está desativada.", 400, "company_inactive");
        }
        return (companyId, agentId);
    }

    private static async Task CopyLimitedAsync(Stream source, Stream destination, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new AppException($"O arquivo excede o limite de {maxBytes / (1024 * 1024)} MB.", 400, "file_too_large");
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
}
