using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using K2Agents.Api.Controllers;
using K2Agents.Api.Infrastructure;
using K2Agents.Api.Localization;
using K2Agents.Api.Realtime;
using K2Agents.Application;
using K2Agents.Application.Abstractions;
using K2Agents.Infrastructure;
using K2Agents.Infrastructure.Persistence;
using K2Agents.Infrastructure.Persistence.Seed;
using K2Agents.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

// Logger simples até o host configurar o definitivo (CreateBootstrapLogger não suporta vários hosts, como nos testes).
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, logger) => logger
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    // ---------- JWT ----------
    var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
    if (string.IsNullOrWhiteSpace(jwt.Secret))
    {
        if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
            throw new InvalidOperationException("Jwt:Secret não configurado. Defina JWT_SECRET no .env.");
        // Desenvolvimento local sem segredo: chave efêmera (sessões caem a cada reinício).
        jwt.Secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        builder.Configuration["Jwt:Secret"] = jwt.Secret;
        Log.Warning("Jwt:Secret ausente; usando chave efêmera de desenvolvimento.");
    }
    if (Encoding.UTF8.GetByteCount(jwt.Secret) < 32)
        throw new InvalidOperationException("Jwt:Secret deve ter ao menos 32 bytes.");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = K2ClaimTypes.Name,
            RoleClaimType = K2ClaimTypes.Role
        };
        // WebSockets não enviam o cabeçalho Authorization: o SignalR passa o token na query string.
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/inbox"))
                    context.Token = token;
                return Task.CompletedTask;
            }
        };
    });
    builder.Services.AddAuthorization(Policies.Register);

    // ---------- Serviços ----------
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, CurrentUser>();
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddK2RateLimiting(builder.Configuration);
    builder.Services.AddSignalR(o => o.MaximumReceiveMessageSize = 32 * 1024)
        .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddSingleton<IRealtimeNotifier, SignalRNotifier>();
    builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, SubjectUserIdProvider>();

    builder.Services.AddControllers()
        .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

    // nginx (mesma rede Docker) repassa o IP real do cliente; usado no rate limiting e na auditoria.
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("10.0.0.0"), 8));
        o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("172.16.0.0"), 12));
        o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse("192.168.0.0"), 16));
    });

    var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
    builder.Services.AddCors(o =>
    {
        o.AddDefaultPolicy(p => p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials());
        // Widget incorporado em sites de clientes: qualquer origem, SEM cookies/credenciais.
        // A autorização real é feita no servidor (token do canal + domínios permitidos + chave do visitante).
        o.AddPolicy(CorsPolicies.PublicWidget, p => p.AllowAnyOrigin().AllowAnyHeader().WithMethods("GET", "POST"));
    });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "K2 AI Agents API",
            Version = "v1",
            Description = "Plataforma SaaS de agentes de IA para atendimento empresarial — ecossistema K2 Digital."
        });
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Access token obtido em POST /api/v1/auth/login."
        });
        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
        });
    });

    var app = builder.Build();
    // Falha na inicialização (e não no primeiro uso) se a chave de criptografia estiver ausente ou inválida.
    app.Services.GetRequiredService<ISecretProtector>();

    app.UseForwardedHeaders();
    app.UseRequestLanguage();
    // Log de requisições por fora do tratador de exceções: registra o status final (ex.: 401), não 500.
    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();

    if (app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.DocumentTitle = "K2 AI Agents API");
    }

    app.UseCors();
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();
    app.MapControllers();
    app.MapHub<InboxHub>("/hubs/inbox");
    app.MapHub<WebChatHub>("/hubs/webchat").RequireCors(CorsPolicies.PublicWidget);
    app.MapHealthChecks("/health").DisableRateLimiting();

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (app.Configuration.GetValue("Database:MigrateOnStartup", false) && db.Database.IsRelational())
        {
            Log.Information("Aplicando migrations do banco de dados...");
            await db.Database.MigrateAsync();
        }
        if (app.Configuration.GetValue("Database:Seed", false))
            await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync();
    }

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "A API foi encerrada de forma inesperada.");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
