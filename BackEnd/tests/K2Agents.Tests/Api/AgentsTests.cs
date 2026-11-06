using System.Net;
using System.Text.Json;
using K2Agents.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Tests.Api;

public class AgentsTests(K2ApiFactory factory) : IClassFixture<K2ApiFactory>
{
    private static object NewAgent(string name = "Agente Teste", string? systemPrompt = null, string model = "gpt-4o-mini") => new
    {
        name,
        description = "Agente criado no teste",
        businessType = "Support",
        responseLanguage = "pt-BR",
        behavior = new
        {
            objective = "Ajudar clientes com dúvidas sobre pedidos.",
            personality = "Paciente",
            tone = "Cordial",
            businessHours = "Seg a sex, 9h às 18h",
            faqs = new[] { new { question = "Qual o prazo de entrega?", answer = "Até 3 dias úteis." } },
            restrictions = new[] { "Não inventar prazos." },
            handoffRules = new[] { "Reclamações formais." }
        },
        systemPrompt,
        welcomeMessage = "Olá!",
        model,
        temperature = 0.3,
        maxOutputTokens = 500
    };

    private async Task<JsonElement> CreateAsPadariaAsync(string name = "Agente Teste")
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var response = await client.PostJsonAsync("/api/v1/agents", NewAgent(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    [Fact]
    public async Task Seed_CreatesFiveActiveDemoAgents()
    {
        var admin = await factory.AuthenticatedAsync(Demo.AdminEmail, Demo.AdminPassword);
        var list = await (await admin.GetAsync("/api/v1/agents?pageSize=100&status=Active")).ReadJsonAsync();
        var names = list.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("name").GetString()).ToList();
        foreach (var expected in new[] { "Ana", "Bella", "Lucas", "Nina", "Max" }) Assert.Contains(expected, names);
    }

    [Fact]
    public async Task Create_GeneratesPromptFromBehavior_AndStartsAsDraftV1()
    {
        var agent = await CreateAsPadariaAsync();

        Assert.Equal("Draft", agent.GetProperty("status").GetString());
        Assert.Equal(1, agent.GetProperty("currentVersion").GetInt32());
        var prompt = agent.GetProperty("systemPrompt").GetString()!;
        Assert.Contains("Você é Agente Teste, assistente virtual da empresa Padaria Jardim Andrade", prompt);
        Assert.Contains("Qual o prazo de entrega?", prompt);
        Assert.Contains("Não inventar prazos.", prompt);
        Assert.Contains("Reclamações formais.", prompt);
        Assert.Equal("Cordial", agent.GetProperty("behavior").GetProperty("tone").GetString());
    }

    [Fact]
    public async Task Create_KeepsUserReviewedPrompt()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var response = await client.PostJsonAsync("/api/v1/agents", NewAgent(systemPrompt: "Prompt revisado manualmente."));
        Assert.Equal("Prompt revisado manualmente.", (await response.ReadJsonAsync()).GetProperty("systemPrompt").GetString());
    }

    [Fact]
    public async Task PreviewPrompt_ReturnsBusinessPromptAndPlatformRulesSeparately()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var response = await client.PostJsonAsync("/api/v1/agents/preview-prompt", new
        {
            name = "Ana",
            businessType = "Sales",
            responseLanguage = "en",
            behavior = new { objective = "Sell bread.", restrictions = new[] { "Do not make up prices." } }
        });
        var json = await response.ReadJsonAsync();

        var prompt = json.GetProperty("prompt").GetString()!;
        Assert.Contains("You are Ana, the virtual assistant of Padaria Jardim Andrade", prompt);
        Assert.Contains("Always reply in English.", prompt);
        Assert.DoesNotContain("Platform rules", prompt);
        Assert.Contains("<knowledge>", json.GetProperty("platformRules").GetString());
    }

    [Fact]
    public async Task Update_ConfigChangeCreatesVersion_NameOnlyChangeDoesNot()
    {
        var created = await CreateAsPadariaAsync("Versionado");
        var id = created.GetProperty("id").GetString();
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);

        var renamed = await (await client.PutJsonAsync($"/api/v1/agents/{id}", NewAgent("Versionado Renomeado", created.GetProperty("systemPrompt").GetString())))
            .ReadJsonAsync();
        Assert.Equal(1, renamed.GetProperty("currentVersion").GetInt32());

        var changed = await (await client.PutJsonAsync($"/api/v1/agents/{id}", NewAgent("Versionado Renomeado", "Novo prompt v2")))
            .ReadJsonAsync();
        Assert.Equal(2, changed.GetProperty("currentVersion").GetInt32());

        var versions = await (await client.GetAsync($"/api/v1/agents/{id}/versions")).ReadJsonAsync();
        Assert.Equal(2, versions.GetArrayLength());
        Assert.True(versions[0].GetProperty("isCurrent").GetBoolean());
        Assert.Equal("Novo prompt v2", versions[0].GetProperty("systemPrompt").GetString());
        Assert.Equal("Admin Padaria Jardim Andrade", versions[0].GetProperty("createdByName").GetString());
    }

    [Fact]
    public async Task RestoreVersion_CreatesNewVersionWithOldConfig()
    {
        var created = await CreateAsPadariaAsync("Restaurável");
        var id = created.GetProperty("id").GetString();
        var original = created.GetProperty("systemPrompt").GetString();
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        await client.PutJsonAsync($"/api/v1/agents/{id}", NewAgent("Restaurável", "Prompt alterado"));

        var restored = await (await client.PostAsync($"/api/v1/agents/{id}/versions/1/restore", null)).ReadJsonAsync();

        Assert.Equal(3, restored.GetProperty("currentVersion").GetInt32());
        Assert.Equal(original, restored.GetProperty("systemPrompt").GetString());
    }

    [Fact]
    public async Task Duplicate_CreatesDraftCopyInSameCompany()
    {
        var created = await CreateAsPadariaAsync("Original");
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);

        var response = await client.PostAsync($"/api/v1/agents/{created.GetProperty("id").GetString()}/duplicate", null);
        var copy = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Original (cópia)", copy.GetProperty("name").GetString());
        Assert.Equal("Draft", copy.GetProperty("status").GetString());
        Assert.Equal(created.GetProperty("systemPrompt").GetString(), copy.GetProperty("systemPrompt").GetString());
        Assert.Equal(created.GetProperty("companyId").GetString(), copy.GetProperty("companyId").GetString());
    }

    [Fact]
    public async Task Status_ActivateAndDeactivate_AreAudited()
    {
        var created = await CreateAsPadariaAsync("Ativável");
        var id = created.GetProperty("id").GetString();
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);

        var active = await (await client.PatchJsonAsync($"/api/v1/agents/{id}/status", new { status = "Active" })).ReadJsonAsync();
        Assert.Equal("Active", active.GetProperty("status").GetString());
        var inactive = await (await client.PatchJsonAsync($"/api/v1/agents/{id}/status", new { status = "Inactive" })).ReadJsonAsync();
        Assert.Equal("Inactive", inactive.GetProperty("status").GetString());

        await using var db = factory.SystemDb();
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.Action == "agent.status_changed" && a.EntityId == id));
    }

    [Fact]
    public async Task Tenancy_CompanyCannotSeeOrEditAnotherCompanysAgents()
    {
        var created = await CreateAsPadariaAsync("Privado da Padaria");
        var id = created.GetProperty("id").GetString();
        var bella = await factory.AuthenticatedAsync(Demo.BellaAdmin);

        var list = await (await bella.GetAsync("/api/v1/agents?pageSize=100")).ReadJsonAsync();
        Assert.All(list.GetProperty("items").EnumerateArray(), a => Assert.Equal("Studio Bella", a.GetProperty("companyName").GetString()));

        Assert.Equal(HttpStatusCode.NotFound, (await bella.GetAsync($"/api/v1/agents/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bella.PutJsonAsync($"/api/v1/agents/{id}", NewAgent())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bella.PostAsync($"/api/v1/agents/{id}/duplicate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bella.GetAsync($"/api/v1/agents/{id}/versions")).StatusCode);
    }

    [Fact]
    public async Task Tenancy_CompanyCannotCreateAgentForAnotherCompany()
    {
        Guid bellaId;
        await using (var db = factory.SystemDb()) bellaId = (await db.Companies.FirstAsync(c => c.Name == "Studio Bella")).Id;
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);

        var body = JsonSerializer.SerializeToElement(NewAgent());
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(body.GetRawText())!;
        dict["companyId"] = bellaId;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostJsonAsync("/api/v1/agents", dict)).StatusCode);
    }

    [Theory]
    [InlineData(Demo.PadariaViewer)]
    [InlineData(Demo.PadariaAttendant)]
    public async Task ReadOnlyRoles_CanListButCannotCreate(string email)
    {
        var client = await factory.AuthenticatedAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/agents")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostJsonAsync("/api/v1/agents", NewAgent())).StatusCode);
    }

    [Fact]
    public async Task AgentManager_CanCreate()
    {
        var client = await factory.AuthenticatedAsync("gestor@padaria.demo");
        Assert.Equal(HttpStatusCode.Created, (await client.PostJsonAsync("/api/v1/agents", NewAgent("Do Gestor"))).StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_MustInformCompany()
    {
        var admin = await factory.AuthenticatedAsync(Demo.AdminEmail, Demo.AdminPassword);
        var response = await admin.PostJsonAsync("/api/v1/agents", NewAgent());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("company_required", (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Validation_RejectsUnknownModelAndOutOfRangeValues()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var response = await client.PostJsonAsync("/api/v1/agents", new
        {
            name = "",
            businessType = "Sales",
            responseLanguage = "fr",
            behavior = new { objective = "" },
            model = "modelo-inexistente",
            temperature = 3,
            maxOutputTokens = 10
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");
        foreach (var field in new[] { "Name", "ResponseLanguage", "Behavior.Objective", "Model", "Temperature", "MaxOutputTokens" })
            Assert.True(errors.TryGetProperty(field, out _), $"Esperava erro em {field}: {errors}");
    }

    [Fact]
    public async Task ModelsAndTemplates_AreAvailable()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaViewer);
        var models = await (await client.GetAsync("/api/v1/agents/models")).ReadJsonAsync();
        Assert.Contains(models.EnumerateArray(), m => m.GetProperty("id").GetString() == "gpt-4o-mini");

        var templates = await (await client.GetAsync("/api/v1/agents/templates?language=en")).ReadJsonAsync();
        Assert.Equal(5, templates.GetArrayLength());
    }
}
