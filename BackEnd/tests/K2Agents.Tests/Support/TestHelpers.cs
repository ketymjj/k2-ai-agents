using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using K2Agents.Application.Abstractions;
using K2Agents.Domain.Enums;
using K2Agents.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace K2Agents.Tests.Support;

/// <summary>Usuário fictício controlado pelo teste.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; set; }
    public Guid? UserId { get; set; }
    public Guid? CompanyId { get; set; }
    public UserRole? Role { get; set; }
    public bool IsSuperAdmin => Role == UserRole.SuperAdmin;
    public string? IpAddress => "127.0.0.1";

    public static FakeCurrentUser System() => new();

    public static FakeCurrentUser Of(Guid companyId, UserRole role = UserRole.CompanyAdmin) =>
        new() { IsAuthenticated = true, UserId = Guid.NewGuid(), CompanyId = companyId, Role = role };

    public static FakeCurrentUser SuperAdmin() =>
        new() { IsAuthenticated = true, UserId = Guid.NewGuid(), Role = UserRole.SuperAdmin };
}

public static class TestDb
{
    /// <summary>Cria um contexto InMemory; o mesmo <paramref name="name"/> compartilha os dados entre contextos.</summary>
    public static AppDbContext Create(string name, ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options, user);
}

/// <summary>Sobe a API em memória trocando o PostgreSQL por um banco InMemory isolado, com o seed de demonstração.</summary>
public class K2ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"k2-tests-{Guid.NewGuid()}";

    protected virtual int AuthPerMinute => 10_000;

    /// <summary>Cliente do WhatsApp simulado, registrado em todas as fábricas de teste.</summary>
    public FakeWhatsAppClient WhatsApp { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Database:Seed", "true");
        builder.UseSetting("Jwt:Secret", "chave-de-teste-com-mais-de-32-bytes-0123456789");
        builder.UseSetting("RateLimiting:AuthPerMinute", AuthPerMinute.ToString());
        builder.UseSetting("Retention:Enabled", "false"); // nos testes a retenção é chamada diretamente
        builder.ConfigureServices(services =>
        {
            var descriptors = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                d.ServiceType == typeof(DbContextOptions)).ToList();
            foreach (var d in descriptors) services.Remove(d);

            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
            services.AddSingleton<Application.Abstractions.IWhatsAppClient>(WhatsApp);
            ConfigureTestServices(services);
        });
    }

    /// <summary>Ponto de extensão para fábricas derivadas substituírem serviços.</summary>
    protected virtual void ConfigureTestServices(IServiceCollection services) { }

    public AppDbContext SystemDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, FakeCurrentUser.System());
}

public class StrictRateLimitFactory : K2ApiFactory
{
    protected override int AuthPerMinute => 3;
}

public static class Demo
{
    public const string AdminEmail = "admin@k2digital.com";
    public const string AdminPassword = "K2Admin@2026";
    public const string Password = "Demo@2026";
    public const string PadariaAdmin = "admin@padaria.demo";
    public const string PadariaAttendant = "atendente@padaria.demo";
    public const string PadariaViewer = "consulta@padaria.demo";
    public const string BellaAdmin = "admin@studiobella.demo";
}

public static class Http
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json);

    public static Task<HttpResponseMessage> LoginAsync(this HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    /// <summary>Cliente já autenticado (Bearer) para o usuário informado.</summary>
    public static async Task<HttpClient> AuthenticatedAsync(this K2ApiFactory factory, string email, string password = Demo.Password)
    {
        var client = factory.CreateClient();
        var response = await client.LoginAsync(email, password);
        response.EnsureSuccessStatusCode();
        var token = (await response.ReadJsonAsync()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(url, body, Json);

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, object body) =>
        client.PutAsJsonAsync(url, body, Json);

    public static Task<HttpResponseMessage> PatchJsonAsync(this HttpClient client, string url, object body) =>
        client.PatchAsJsonAsync(url, body, Json);
}
