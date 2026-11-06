using System.Net;
using System.Text;
using K2Agents.Application.Abstractions;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using K2Agents.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;

namespace K2Agents.Tests.Api;

public class ReportsTests(K2ApiFactory factory) : IClassFixture<K2ApiFactory>
{
    private async Task<T> WithDbAsync<T>(Func<IAppDbContext, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<IAppDbContext>());
    }

    private Task<Guid> CompanyIdAsync(string name) =>
        WithDbAsync(db => db.Companies.IgnoreQueryFilters().Where(c => c.Name == name).Select(c => c.Id).FirstAsync());

    [Fact]
    public async Task Dashboard_IsConsistent_WithSeededData()
    {
        var admin = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var d = await (await admin.GetAsync("/api/v1/reports/dashboard")).ReadJsonAsync();

        var kpis = d.GetProperty("kpis");
        var conversations = kpis.GetProperty("conversations").GetInt32();
        Assert.True(conversations > 0, "o seed tem conversas nos últimos 30 dias");
        Assert.Equal(30, d.GetProperty("period").GetProperty("days").GetInt32());
        Assert.Equal(30, d.GetProperty("daily").GetArrayLength());
        Assert.Equal(conversations, d.GetProperty("daily").EnumerateArray().Sum(p => p.GetProperty("conversations").GetInt32()));
        Assert.Equal(conversations, d.GetProperty("byChannel").EnumerateArray().Sum(c => c.GetProperty("conversations").GetInt32()));
        Assert.Equal(kpis.GetProperty("aiCalls").GetInt32(), d.GetProperty("byChannel").EnumerateArray().Sum(c => c.GetProperty("aiCalls").GetInt32()));
        Assert.Equal(conversations, d.GetProperty("byAgent").EnumerateArray().Sum(a => a.GetProperty("conversations").GetInt32()));
        Assert.Equal(conversations, d.GetProperty("conversationsByStatus").EnumerateArray().Sum(s => s.GetProperty("count").GetInt32()));
        Assert.True(kpis.GetProperty("resolvedByAI").GetInt32() <= kpis.GetProperty("closedConversations").GetInt32());
        // As conversas do seed são sinalizadas como demonstração para o painel avisar o usuário.
        Assert.InRange(d.GetProperty("demoConversations").GetInt32(), 1, conversations);

        // Conferência direta no banco (empresa da Padaria, sem Playground).
        var padaria = await CompanyIdAsync("Padaria Jardim Andrade");
        var from = DateTime.UtcNow.AddDays(-31);
        var expectedMin = await WithDbAsync(db => db.Conversations.IgnoreQueryFilters()
            .CountAsync(c => c.CompanyId == padaria && c.Channel != ChannelType.Playground && c.StartedAt >= DateTime.UtcNow.AddDays(-29)));
        var expectedMax = await WithDbAsync(db => db.Conversations.IgnoreQueryFilters()
            .CountAsync(c => c.CompanyId == padaria && c.Channel != ChannelType.Playground && c.StartedAt >= from));
        Assert.InRange(conversations, expectedMin, expectedMax);
    }

    [Fact]
    public async Task Dashboard_CountsRealAICost_AndIgnoresSimulatedReplies()
    {
        var bella = await CompanyIdAsync("Studio Bella");
        var admin = await factory.AuthenticatedAsync(Demo.BellaAdmin);
        var before = (await (await admin.GetAsync("/api/v1/reports/dashboard")).ReadJsonAsync()).GetProperty("kpis");

        await WithDbAsync(async db =>
        {
            db.AIUsageRecords.Add(new AIUsageRecord { CompanyId = bella, Provider = "OpenAI", Model = "gpt-test", InputTokens = 1000, OutputTokens = 200,
                EstimatedCostUsd = 0.0125m, Success = true, Channel = ChannelType.WebChat });
            db.AIUsageRecords.Add(new AIUsageRecord { CompanyId = bella, Provider = "Simulated", Model = "simulated", IsSimulated = true, Success = true,
                Channel = ChannelType.WebChat });
            return await db.SaveChangesAsync();
        });

        var after = (await (await admin.GetAsync("/api/v1/reports/dashboard")).ReadJsonAsync()).GetProperty("kpis");
        Assert.Equal(before.GetProperty("aiCalls").GetInt32() + 1, after.GetProperty("aiCalls").GetInt32());
        Assert.Equal(before.GetProperty("estimatedCostUsd").GetDecimal() + 0.0125m, after.GetProperty("estimatedCostUsd").GetDecimal());
        Assert.Equal(before.GetProperty("inputTokens").GetInt64() + 1000, after.GetProperty("inputTokens").GetInt64());
    }

    [Fact]
    public async Task Dashboard_EnforcesTenant_AndValidatesPeriod()
    {
        var bella = await CompanyIdAsync("Studio Bella");
        var padariaAdmin = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await padariaAdmin.GetAsync($"/api/v1/reports/dashboard?companyId={bella}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await padariaAdmin.GetAsync("/api/v1/reports/dashboard?from=2026-05-10&to=2026-05-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await padariaAdmin.GetAsync("/api/v1/reports/dashboard?from=2024-01-01&to=2026-01-01")).StatusCode);

        // SuperAdmin pode ver a plataforma inteira ou uma empresa específica.
        var super = await factory.AuthenticatedAsync(Demo.AdminEmail, Demo.AdminPassword);
        var all = (await (await super.GetAsync("/api/v1/reports/dashboard")).ReadJsonAsync()).GetProperty("kpis").GetProperty("conversations").GetInt32();
        var one = (await (await super.GetAsync($"/api/v1/reports/dashboard?companyId={bella}")).ReadJsonAsync()).GetProperty("kpis").GetProperty("conversations").GetInt32();
        Assert.True(all >= one);
    }

    [Fact]
    public async Task CsvExport_RequiresManager_IsExcelFriendly_AndNeutralizesFormulas()
    {
        var viewer = await factory.AuthenticatedAsync(Demo.PadariaViewer);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/reports/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/v1/reports/export/Conversations")).StatusCode);

        var admin = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var created = await admin.PostJsonAsync("/api/v1/contacts", new { name = "=HYPERLINK(\"http://evil\")", phone = "11977776666", isLead = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var leads = await admin.GetAsync("/api/v1/reports/export/Leads");
        Assert.Equal(HttpStatusCode.OK, leads.StatusCode);
        Assert.StartsWith("text/csv", leads.Content.Headers.ContentType!.ToString());
        var bytes = await leads.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes[3..]);
        Assert.StartsWith("Criado em;Nome;E-mail;Telefone", text);
        Assert.Contains("\"'=HYPERLINK(\"\"http://evil\"\")\"", text);
        Assert.DoesNotContain(";=HYPERLINK", text);

        var kpis = (await (await admin.GetAsync("/api/v1/reports/dashboard")).ReadJsonAsync()).GetProperty("kpis");
        var conversationsCsv = await (await admin.GetAsync("/api/v1/reports/export/Conversations")).Content.ReadAsStringAsync();
        var lines = conversationsCsv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(kpis.GetProperty("conversations").GetInt32() + 1, lines.Length);
        Assert.DoesNotContain(";Closed;", conversationsCsv);
        Assert.DoesNotContain(";Open;", conversationsCsv);

        var english = await (await admin.GetAsync("/api/v1/reports/export/Appointments?lang=en")).Content.ReadAsStringAsync();
        Assert.StartsWith("﻿Start,End,Service", english);

        var exported = await WithDbAsync(db => db.AuditLogs.CountAsync(a => a.Action == "report.exported"));
        Assert.True(exported >= 3);
    }

    [Fact]
    public async Task SummaryPdf_IsValid_AndKeepsPortugueseAccents()
    {
        var viewer = await factory.AuthenticatedAsync(Demo.PadariaViewer);
        var response = await viewer.GetAsync("/api/v1/reports/summary.pdf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        using var pdf = PdfDocument.Open(bytes);
        var text = string.Join(" ", pdf.GetPages().Select(p => p.Text));
        Assert.Contains("Relatório de atendimento", text);
        Assert.Contains("Padaria Jardim Andrade", text);
        Assert.Contains("Transferidas para humano", text);
        Assert.Contains("estimados", text);
    }

    [Fact]
    public async Task Costs_CompareEstimatedWithBilled_OnlyForSuperAdmin()
    {
        var month = DateTime.UtcNow.AddHours(-3).ToString("yyyy-MM");
        var super = await factory.AuthenticatedAsync(Demo.AdminEmail, Demo.AdminPassword);
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PutJsonAsync("/api/v1/reports/billing", new { month = "2026-13", amountUsd = 1 })).StatusCode);

        var saved = await super.PutJsonAsync("/api/v1/reports/billing", new { month, amountUsd = 2.5m, notes = "Fatura de teste" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        // Upsert: lançar de novo o mesmo mês substitui o valor.
        await super.PutJsonAsync("/api/v1/reports/billing", new { month, amountUsd = 3m, notes = "Fatura corrigida" });

        var costs = await (await super.GetAsync($"/api/v1/reports/costs?from={month}&to={month}")).ReadJsonAsync();
        Assert.True(costs.GetProperty("canSeeBilling").GetBoolean());
        var m = costs.GetProperty("months")[0];
        Assert.Equal(3m, m.GetProperty("billedUsd").GetDecimal());
        Assert.Equal("Fatura corrigida", m.GetProperty("billedNotes").GetString());
        Assert.Equal(3m - m.GetProperty("estimatedUsd").GetDecimal(), m.GetProperty("differenceUsd").GetDecimal());

        var companyAdmin = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var own = await (await companyAdmin.GetAsync($"/api/v1/reports/costs?from={month}&to={month}")).ReadJsonAsync();
        Assert.False(own.GetProperty("canSeeBilling").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, own.GetProperty("months")[0].GetProperty("billedUsd").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await companyAdmin.PutJsonAsync("/api/v1/reports/billing", new { month, amountUsd = 1 })).StatusCode);

        var attendant = await factory.AuthenticatedAsync(Demo.PadariaAttendant);
        Assert.Equal(HttpStatusCode.Forbidden, (await attendant.GetAsync("/api/v1/reports/costs")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await super.DeleteAsync($"/api/v1/reports/billing/{month}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await super.DeleteAsync($"/api/v1/reports/billing/{month}")).StatusCode);
    }
}
