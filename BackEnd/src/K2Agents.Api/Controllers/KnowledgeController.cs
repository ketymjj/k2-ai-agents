using K2Agents.Api.Infrastructure;
using K2Agents.Application.Common;
using K2Agents.Application.Knowledge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace K2Agents.Api.Controllers;

[ApiController]
[Route("api/v1/knowledge")]
[Authorize(Policy = Policies.Reader)]
public class KnowledgeController(IKnowledgeService knowledge) : ControllerBase
{
    /// <summary>Limite do corpo multipart: arquivo (10 MB) + campos do formulário.</summary>
    private const long UploadRequestLimit = 11 * 1024 * 1024;

    [HttpGet]
    public Task<PagedResult<KnowledgeListItemDto>> List([FromQuery] KnowledgeQuery query, CancellationToken ct) => knowledge.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<KnowledgeDocumentDto> Get(Guid id, CancellationToken ct) => knowledge.GetAsync(id, ct);

    [HttpPost("search")]
    public Task<IReadOnlyList<KnowledgeSearchHit>> Search(SearchKnowledgeRequest request, CancellationToken ct) => knowledge.SearchAsync(request, ct);

    [HttpPost]
    [Authorize(Policy = Policies.AgentManager)]
    public async Task<ActionResult<KnowledgeDocumentDto>> Create(CreateKnowledgeRequest request, CancellationToken ct)
    {
        var created = await knowledge.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPost("upload")]
    [Authorize(Policy = Policies.AgentManager)]
    [RequestSizeLimit(UploadRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadRequestLimit)]
    public async Task<ActionResult<KnowledgeDocumentDto>> Upload(
        IFormFile? file, [FromForm] Guid? agentId, [FromForm] string? title, [FromForm] Guid? companyId, CancellationToken ct)
    {
        if (file is null) throw new AppException("Selecione um arquivo.", 400, "file_required");
        await using var stream = file.OpenReadStream();
        var created = await knowledge.UploadAsync(new UploadKnowledgeRequest(stream, file.FileName, file.Length, agentId, title, companyId), ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.AgentManager)]
    public Task<KnowledgeDocumentDto> Update(Guid id, UpdateKnowledgeRequest request, CancellationToken ct) => knowledge.UpdateAsync(id, request, ct);

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = Policies.AgentManager)]
    public Task<KnowledgeDocumentDto> SetStatus(Guid id, SetKnowledgeStatusRequest request, CancellationToken ct)
        => knowledge.SetStatusAsync(id, request.Status, ct);

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.AgentManager)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await knowledge.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var (content, fileName) = await knowledge.DownloadAsync(id, ct);
        if (!new FileExtensionContentTypeProvider().TryGetContentType(fileName, out var contentType))
            contentType = "application/octet-stream";
        // Sempre como anexo: o navegador nunca renderiza o arquivo enviado na origem da aplicação.
        return File(content, contentType, fileName);
    }
}
