using K2Agents.Application.Abstractions;
using K2Agents.Application.Conversations;
using K2Agents.Application.Knowledge;
using K2Agents.Application.Reports;
using K2Agents.Infrastructure.Reports;
using K2Agents.Infrastructure.AI;
using K2Agents.Infrastructure.Knowledge;
using K2Agents.Infrastructure.Persistence;
using K2Agents.Infrastructure.Persistence.Seed;
using K2Agents.Infrastructure.Security;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace K2Agents.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default não configurada.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<DataSeeder>();
        services.AddSingleton<IModelCatalog, ModelCatalog>();

        services.Configure<OpenAIOptions>(configuration.GetSection(OpenAIOptions.Section));
        services.AddHttpClient<IAIProvider, OpenAIProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton(configuration.GetSection("AI:Limits").Get<AIUsageSettings>() ?? new AIUsageSettings());

        services.AddSingleton(configuration.GetSection("Knowledge").Get<KnowledgeSettings>() ?? new KnowledgeSettings());
        services.AddSingleton<ITextExtractor, TextExtractor>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddScoped<IKnowledgeSearchService, KnowledgeSearchService>();

        services.AddSingleton<IReportPdfRenderer, PdfReportRenderer>();
        services.AddHostedService<Privacy.RetentionWorker>();

        services.AddHttpClient<IWhatsAppClient, WhatsApp.WhatsAppCloudClient>();
        services.AddSingleton<WhatsApp.WhatsAppInboxQueue>();
        services.AddSingleton<Application.WhatsApp.IWhatsAppInbox>(sp => sp.GetRequiredService<WhatsApp.WhatsAppInboxQueue>());
        services.AddHostedService<WhatsApp.WhatsAppInboxWorker>();

        // Chave obrigatória fora de Development/Testing; nesses dois, sem chave, usa uma temporária (segredos não sobrevivem ao restart).
        services.AddSingleton<ISecretProtector>(sp =>
        {
            var configured = configuration["Secrets:EncryptionKey"];
            var env = sp.GetRequiredService<IHostEnvironment>();
            if (string.IsNullOrWhiteSpace(configured) && (env.IsDevelopment() || env.IsEnvironment("Testing")))
            {
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<AesGcmSecretProtector>()
                    .LogWarning("Secrets:EncryptionKey ausente; usando chave temporária de desenvolvimento.");
                return new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32));
            }
            return new AesGcmSecretProtector(AesGcmSecretProtector.ParseKey(configured));
        });

        return services;
    }
}
