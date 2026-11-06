using K2Agents.Application.Knowledge;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using K2Agents.Infrastructure.Knowledge;
using K2Agents.Infrastructure.Persistence;
using K2Agents.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace K2Agents.Tests.Knowledge;

/// <summary>
/// PostgreSQL real (Testcontainers): aplica as migrations de verdade e valida a busca textual
/// (tsvector, dicionário português, GIN, ranking). Requer Docker.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Guid CompanyA { get; } = Guid.NewGuid();
    public Guid CompanyB { get; } = Guid.NewGuid();
    public Guid AgentA1 { get; } = Guid.NewGuid();
    public Guid AgentA2 { get; } = Guid.NewGuid();
    public Guid ArchivedDocId { get; private set; }

    public string ConnectionString => _container.GetConnectionString();

    public AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_container.GetConnectionString()).Options, FakeCurrentUser.System());

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDb();
        await db.Database.MigrateAsync();

        db.Companies.AddRange(new Company { Id = CompanyA, Name = "Padaria" }, new Company { Id = CompanyB, Name = "Outra" });
        db.AIAgents.AddRange(
            new AIAgent { Id = AgentA1, CompanyId = CompanyA, Name = "Ana", SystemPrompt = "x" },
            new AIAgent { Id = AgentA2, CompanyId = CompanyA, Name = "Beto", SystemPrompt = "x" });

        Add(db, CompanyA, null, "Cardápio", "Pão francês custa R$ 0,90 a unidade. Bolo de cenoura custa R$ 38,00.");
        Add(db, CompanyA, null, "Encomendas", "Aceitamos encomendas de bolos de festa com 48 horas de antecedência.");
        Add(db, CompanyA, AgentA2, "Exclusivo do Beto", "Senha do wi-fi da loja: padaria2026.");
        ArchivedDocId = Add(db, CompanyA, null, "Antigo", "Promoção de sonho encerrada em 2020.", KnowledgeStatus.Archived);
        Add(db, CompanyB, null, "Concorrente", "Pão francês custa R$ 0,50 na concorrência.");
        await db.SaveChangesAsync();
    }

    private static Guid Add(AppDbContext db, Guid companyId, Guid? agentId, string title, string content, KnowledgeStatus status = KnowledgeStatus.Ready)
    {
        var doc = new KnowledgeDocument { CompanyId = companyId, AgentId = agentId, Title = title, Content = content, Status = status };
        db.KnowledgeDocuments.Add(doc);
        db.KnowledgeChunks.AddRange(KnowledgeIndexer.BuildChunks(doc));
        return doc.Id;
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

public class PostgresFullTextSearchTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    private async Task<IReadOnlyList<Application.Abstractions.KnowledgeSnippet>> Search(Guid company, Guid? agent, string query)
    {
        await using var db = pg.CreateDb();
        return await new KnowledgeSearchService(db).SearchAsync(company, agent, query, 5, CancellationToken.None);
    }

    [Fact]
    public async Task FindsRelevantChunk_ForNaturalLanguageQuestion()
    {
        var hits = await Search(pg.CompanyA, pg.AgentA1, "Quanto custa o pão francês?");
        Assert.Equal("Cardápio", hits[0].Title);
        Assert.True(hits[0].Score > 0);
    }

    [Theory]
    [InlineData("quanto custa o pao frances")]   // cliente digitando sem acentos
    [InlineData("PÃO FRANCÊS")]                  // maiúsculas com acentos
    public async Task Search_IsAccentAndCaseInsensitive(string query)
    {
        var hits = await Search(pg.CompanyA, pg.AgentA1, query);
        Assert.Equal("Cardápio", hits[0].Title);
    }

    [Fact]
    public async Task PortugueseStemming_MatchesWordVariations()
    {
        // "encomendar" e "bolo" (singular) encontram "encomendas de bolos".
        var hits = await Search(pg.CompanyA, pg.AgentA1, "Posso encomendar um bolo?");
        Assert.Equal("Encomendas", hits[0].Title);
    }

    [Fact]
    public async Task NeverReturnsOtherCompanysChunks()
    {
        var hits = await Search(pg.CompanyA, pg.AgentA1, "pão francês concorrência");
        Assert.DoesNotContain(hits, h => h.Title == "Concorrente");
    }

    [Fact]
    public async Task RespectsAgentScope()
    {
        Assert.DoesNotContain(await Search(pg.CompanyA, pg.AgentA1, "senha wi-fi loja"), h => h.Title == "Exclusivo do Beto");
        Assert.Contains(await Search(pg.CompanyA, pg.AgentA2, "senha wi-fi loja"), h => h.Title == "Exclusivo do Beto");
    }

    [Fact]
    public async Task ExcludesArchivedDocuments()
    {
        var hits = await Search(pg.CompanyA, pg.AgentA1, "promoção sonho");
        Assert.DoesNotContain(hits, h => h.DocumentId == pg.ArchivedDocId);
    }

    [Theory]
    [InlineData("'); DROP TABLE \"KnowledgeChunks\"; --")]
    [InlineData("pão & | ! :* <-> ()")]
    [InlineData("o a de")]
    public async Task HostileOrEmptyQueries_AreSafe(string query)
    {
        await Search(pg.CompanyA, pg.AgentA1, query);
        Assert.NotEmpty(await Search(pg.CompanyA, pg.AgentA1, "pão francês")); // tabela intacta
    }

    [Fact]
    public async Task Migrations_CreateGinIndexOnSearchVector()
    {
        await using var db = pg.CreateDb();
        var indexes = await db.Database.SqlQuery<string>(
            $"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'KnowledgeChunks' AND indexname = 'IX_KnowledgeChunks_SearchVector'").ToListAsync();
        Assert.Contains("gin", Assert.Single(indexes), StringComparison.OrdinalIgnoreCase);
    }
}
