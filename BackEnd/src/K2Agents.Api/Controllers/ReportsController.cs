using K2Agents.Api.Infrastructure;
using K2Agents.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace K2Agents.Api.Controllers;

[ApiController]
[Route("api/v1/reports")]
[Authorize(Policy = Policies.Reader)]
public class ReportsController(IReportService reports) : ControllerBase
{
    /// <summary>Indicadores agregados (sem dados pessoais): disponível para todos os perfis.</summary>
    [HttpGet("dashboard")]
    [HttpGet("/api/v1/dashboard")]
    public Task<DashboardDto> Dashboard([FromQuery] ReportQuery query, CancellationToken ct) => reports.DashboardAsync(query, ct);

    [HttpGet("summary.pdf")]
    public async Task<IActionResult> SummaryPdf([FromQuery] ReportQuery query, CancellationToken ct)
    {
        var file = await reports.SummaryPdfAsync(query, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>Exportações linha a linha contêm dados pessoais (nomes, telefones): restritas a gestores.</summary>
    [HttpGet("export/{type}")]
    [Authorize(Policy = Policies.AgentManager)]
    public async Task<IActionResult> Export(ReportExportType type, [FromQuery] ReportQuery query, CancellationToken ct)
    {
        var file = await reports.ExportCsvAsync(type, query, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("costs")]
    [Authorize(Policy = Policies.CompanyAdmin)]
    public Task<CostReportDto> Costs([FromQuery] Guid? companyId, [FromQuery] string? from, [FromQuery] string? to, CancellationToken ct) =>
        reports.CostsAsync(companyId, from, to, ct);

    [HttpPut("billing")]
    [Authorize(Policy = Policies.PlatformAdmin)]
    public Task<CostMonthDto> SaveBilling(SaveBillingRequest request, CancellationToken ct) => reports.SaveBillingAsync(request, ct);

    [HttpDelete("billing/{month}")]
    [Authorize(Policy = Policies.PlatformAdmin)]
    public async Task<IActionResult> DeleteBilling(string month, CancellationToken ct)
    {
        await reports.DeleteBillingAsync(month, ct);
        return NoContent();
    }
}
