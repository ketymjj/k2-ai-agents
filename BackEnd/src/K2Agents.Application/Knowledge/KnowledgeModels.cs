using FluentValidation;
using K2Agents.Application.Common;
using K2Agents.Domain.Enums;

namespace K2Agents.Application.Knowledge;

/// <summary>Limites da base de conhecimento (Knowledge:*).</summary>
public class KnowledgeSettings
{
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxTextChars { get; set; } = 200_000;
    public int MaxPdfPages { get; set; } = 300;
    /// <summary>Orçamento de caracteres de conhecimento enviado ao modelo por resposta.</summary>
    public int MaxContextChars { get; set; } = 3_000;
    public int MaxSnippets { get; set; } = 4;
}

public record KnowledgeListItemDto(
    Guid Id, Guid CompanyId, string? CompanyName, Guid? AgentId, string? AgentName, string Title, KnowledgeSourceType SourceType,
    KnowledgeStatus Status, string? OriginalFileName, long? FileSizeBytes, int ChunkCount, int ContentLength, DateTime CreatedAt, DateTime UpdatedAt);

public record KnowledgeDocumentDto(
    Guid Id, Guid CompanyId, string? CompanyName, Guid? AgentId, string? AgentName, string Title, string Content,
    KnowledgeSourceType SourceType, KnowledgeStatus Status, string? OriginalFileName, long? FileSizeBytes, bool HasFile,
    int ChunkCount, string? ErrorMessage, DateTime CreatedAt, DateTime UpdatedAt);

public record CreateKnowledgeRequest(string Title, string Content, KnowledgeSourceType SourceType, Guid? AgentId, Guid? CompanyId);

public record UpdateKnowledgeRequest(string Title, string Content, Guid? AgentId);

public record SetKnowledgeStatusRequest(KnowledgeStatus Status);

public record SearchKnowledgeRequest(string Query, Guid? AgentId, Guid? CompanyId);

public record KnowledgeSearchHit(Guid DocumentId, string Title, string Content, double Score);

public record UploadKnowledgeRequest(Stream Content, string FileName, long Length, Guid? AgentId, string? Title, Guid? CompanyId);

public record KnowledgeQuery : PagedQuery
{
    public Guid? CompanyId { get; init; }
    public Guid? AgentId { get; init; }
    /// <summary>true = apenas documentos gerais (sem agente específico).</summary>
    public bool? GeneralOnly { get; init; }
    public KnowledgeSourceType? SourceType { get; init; }
    public KnowledgeStatus? Status { get; init; }
    public string? Search { get; init; }
}

public class CreateKnowledgeRequestValidator : AbstractValidator<CreateKnowledgeRequest>
{
    public CreateKnowledgeRequestValidator(KnowledgeSettings settings)
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Informe o título.").MaximumLength(200);
        RuleFor(x => x.Content).NotEmpty().WithMessage("Informe o conteúdo.").MaximumLength(settings.MaxTextChars);
        RuleFor(x => x.SourceType).Must(t => t is KnowledgeSourceType.Text or KnowledgeSourceType.Faq)
            .WithMessage("Use o envio de arquivo para PDF, TXT ou DOCX.");
    }
}

public class UpdateKnowledgeRequestValidator : AbstractValidator<UpdateKnowledgeRequest>
{
    public UpdateKnowledgeRequestValidator(KnowledgeSettings settings)
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Informe o título.").MaximumLength(200);
        RuleFor(x => x.Content).NotEmpty().WithMessage("Informe o conteúdo.").MaximumLength(settings.MaxTextChars);
    }
}

public class SearchKnowledgeRequestValidator : AbstractValidator<SearchKnowledgeRequest>
{
    public SearchKnowledgeRequestValidator()
    {
        RuleFor(x => x.Query).NotEmpty().WithMessage("Digite o que deseja buscar.").MaximumLength(500);
    }
}
