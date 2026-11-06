using K2Agents.Application.Abstractions;
using K2Agents.Application.Agents;
using K2Agents.Application.Automations;
using K2Agents.Application.Channels;
using K2Agents.Application.Scheduling;
using K2Agents.Application.Knowledge;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace K2Agents.Infrastructure.Persistence.Seed;

/// <summary>
/// Dados de demonstração em etapas idempotentes (cada etapa só roda se a sua tabela estiver vazia),
/// para que bancos já existentes recebam o conteúdo de fases novas.
/// Nunca chama provedores de IA (nenhum custo durante o seed).
/// </summary>
public class DataSeeder(
    AppDbContext db, IPasswordHasher hasher, IPromptBuilder promptBuilder, IModelCatalog models,
    IConfiguration configuration, IHostEnvironment hostEnvironment, ILogger<DataSeeder> logger)
{
    public const string DefaultAdminEmail = "admin@k2digital.com";
    public const string DefaultAdminPassword = "K2Admin@2026";
    public const string DefaultDemoPassword = "Demo@2026";

    private record DemoCompany(string Slug, string Name, string Document, string Industry, string Phone, string Website);

    private static readonly DemoCompany[] Companies =
    [
        new("padaria", "Padaria Jardim Andrade", "12345678000101", "Alimentação", "(11) 3456-7801", "https://padariajardimandrade.demo"),
        new("studiobella", "Studio Bella", "12345678000102", "Beleza e Estética", "(11) 3456-7802", "https://studiobella.demo"),
        new("horizonte", "Imobiliária Horizonte", "12345678000103", "Imobiliário", "(11) 3456-7803", "https://imobiliariahorizonte.demo"),
        new("petvida", "Clínica Pet Vida", "12345678000104", "Veterinária", "(11) 3456-7804", "https://clinicapetvida.demo"),
        new("energia", "Academia Energia", "12345678000105", "Fitness", "(11) 3456-7805", "https://academiaenergia.demo")
    ];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedCompaniesAndUsersAsync(ct);
        await SeedAgentsAsync(ct);
        await SeedKnowledgeAsync(ct);
        await SeedChannelsAsync(ct);
        await SeedConversationsAsync(ct);
        await SeedContactsAsync(ct);
        await SeedAutomationsAsync(ct);
        await SeedSchedulingAsync(ct);
    }

    private async Task SeedAutomationsAsync(CancellationToken ct)
    {
        if (await db.AutomationRules.AnyAsync(ct)) return;
        var companies = await db.Companies.Select(c => c.Id).ToListAsync(ct);
        foreach (var id in companies) db.AutomationRules.AddRange(AutomationDefaults.For(id));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: automações padrão para {Count} empresas.", companies.Count);
    }

    private async Task SeedSchedulingAsync(CancellationToken ct)
    {
        if (await db.Appointments.AnyAsync(ct)) return;

        var companies = await db.Companies.ToListAsync(ct);
        var created = 0;
        foreach (var (slug, settings) in DemoScheduling.ByCompany)
        {
            var name = Companies.First(c => c.Slug == slug).Name;
            var company = companies.FirstOrDefault(c => c.Name == name);
            if (company is null) continue;
            if (!SchedulingSettings.Parse(company.SchedulingJson).IsEnabled) company.SchedulingJson = settings.Serialize();

            var agent = await db.AIAgents.FirstOrDefaultAsync(a => a.CompanyId == company.Id, ct);
            var contacts = await db.Contacts.Where(c => c.CompanyId == company.Id).ToListAsync(ct);
            var zone = Availability.Zone(settings);
            var today = DateOnly.FromDateTime(Availability.ToLocal(DateTime.UtcNow, zone));
            var busy = new List<(DateTime Start, DateTime End)>();

            // Passado (concluídos / faltas) e futuro (agendados / confirmados), sempre em horários válidos do expediente.
            int[] dayOffsets = [-6, -3, -1, 1, 2, 4];
            for (var i = 0; i < dayOffsets.Length; i++)
            {
                var date = today.AddDays(dayOffsets[i]);
                var service = settings.Services[i % settings.Services.Count];
                var nowForSlots = dayOffsets[i] < 0 ? Availability.ToUtc(date.ToDateTime(TimeOnly.MinValue), zone).AddDays(-1) : DateTime.UtcNow;
                var slot = Availability.Slots(settings, date, service.DurationMinutes, busy, nowForSlots).Skip(i % 3).FirstOrDefault();
                if (slot is null) continue;
                busy.Add((slot.Start, slot.End));

                db.Appointments.Add(new Appointment
                {
                    CompanyId = company.Id,
                    AgentId = agent?.Id,
                    ContactId = contacts.Count > 0 ? contacts[i % contacts.Count].Id : null,
                    ServiceName = service.Name,
                    StartDateTime = slot.Start,
                    EndDateTime = slot.End,
                    Status = dayOffsets[i] < 0
                        ? (i == 1 ? AppointmentStatus.NoShow : AppointmentStatus.Completed)
                        : (i % 2 == 0 ? AppointmentStatus.Confirmed : AppointmentStatus.Scheduled),
                    Source = i % 2 == 0 ? "Agent" : "Manual",
                    Notes = "Agendamento de demonstração.",
                    CreatedAt = slot.Start.AddDays(-2),
                    UpdatedAt = slot.Start.AddDays(-2)
                });
                created++;
            }
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: agendas configuradas e {Count} agendamentos de demonstração.", created);
    }

    private async Task SeedContactsAsync(CancellationToken ct)
    {
        if (await db.Contacts.AnyAsync(ct)) return;

        var companies = await db.Companies.ToListAsync(ct);
        var demoConversations = await db.Conversations.Where(c => c.IsDemo).OrderBy(c => c.StartedAt).ToListAsync(ct);
        var created = 0;
        foreach (var def in DemoContacts.All)
        {
            var name = Companies.First(c => c.Slug == def.CompanySlug).Name;
            var company = companies.FirstOrDefault(c => c.Name == name);
            if (company is null) continue;

            var when = DateTime.UtcNow.AddDays(-def.DaysAgo).AddHours(-2);
            var contact = new Contact
            {
                CompanyId = company.Id, Name = def.Name, Email = def.Email, Phone = def.Phone, Source = "WebChat",
                Status = def.Status, IsLead = def.Status != ContactStatus.New || def.Email is not null,
                Interest = def.Interest, Notes = def.Notes, CreatedAt = when, UpdatedAt = when
            };
            db.Contacts.Add(contact);

            // Vincula a uma conversa de demonstração da mesma empresa ainda sem contato.
            var conversation = demoConversations.FirstOrDefault(c => c.CompanyId == company.Id && c.ContactId is null);
            if (conversation is not null) conversation.ContactId = contact.Id;
            created++;
        }

        // Conversas de demonstração aguardando atendente geram notificações para a equipe.
        foreach (var waiting in demoConversations.Where(c => c.Status == ConversationStatus.WaitingHuman))
            db.Notifications.Add(new Notification
            {
                CompanyId = waiting.CompanyId, Type = NotificationType.Handoff, Title = "Cliente aguardando atendimento humano",
                Message = "Conversa de demonstração transferida para a equipe.", Link = $"/conversations?id={waiting.Id}",
                CreatedAt = waiting.LastMessageAt
            });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: {Contacts} contatos de demonstração.", created);
    }

    private async Task SeedChannelsAsync(CancellationToken ct)
    {
        if (await db.ChannelConfigurations.AnyAsync(ct)) return;

        var agents = await db.AIAgents.Include(a => a.Company).Where(a => a.Status == AgentStatus.Active).ToListAsync(ct);
        foreach (var agent in agents)
        {
            var settings = new WebChatSettings
            {
                Title = agent.Company!.Name,
                Subtitle = $"Fale com {agent.Name}",
                PrimaryColor = "#6941C6",
                AllowSimulatedReplies = true
            };
            db.ChannelConfigurations.Add(new ChannelConfiguration
            {
                CompanyId = agent.CompanyId,
                AgentId = agent.Id,
                ChannelType = ChannelType.WebChat,
                Name = $"Site {agent.Company.Name}",
                Status = ChannelStatus.Active,
                PublicToken = ChannelService.NewPublicToken(),
                SettingsJson = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
            });
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: {Channels} canais Web Chat de demonstração.", agents.Count);
    }

    private async Task SeedConversationsAsync(CancellationToken ct)
    {
        if (await db.Conversations.AnyAsync(c => c.IsDemo, ct)) return;

        var companies = await db.Companies.ToListAsync(ct);
        var agents = await db.AIAgents.ToListAsync(ct);
        var created = 0;
        foreach (var script in DemoConversations.All)
        {
            var name = Companies.First(c => c.Slug == script.CompanySlug).Name;
            var company = companies.FirstOrDefault(c => c.Name == name);
            var agent = agents.FirstOrDefault(a => a.CompanyId == company?.Id);
            if (company is null || agent is null) continue;

            // Sempre no passado: N dias atrás, recuando algumas horas para "hoje" não cair no futuro.
            var start = DateTime.UtcNow.AddDays(-script.DaysAgo).AddHours(-(1 + created % 5)).AddMinutes(-(created * 7 % 50));
            var conversation = new Conversation
            {
                CompanyId = company.Id,
                AgentId = agent.Id,
                Channel = ChannelType.WebChat,
                Status = script.Status,
                StartedAt = start,
                CreatedAt = start,
                IsDemo = true
            };
            var time = start;
            foreach (var line in script.Lines)
            {
                time = time.AddSeconds(line.Sender == SenderType.Contact ? 45 : 4);
                db.ConversationMessages.Add(new ConversationMessage
                {
                    CompanyId = company.Id,
                    ConversationId = conversation.Id,
                    SenderType = line.Sender,
                    Content = line.Text,
                    CreatedAt = time,
                    ResponseTimeMs = line.Sender == SenderType.AI ? 1200 + line.Text.Length * 8 : null
                });
            }
            conversation.LastMessageAt = time;
            if (script.Status == ConversationStatus.Closed) conversation.ClosedAt = time;
            db.Conversations.Add(conversation);
            created++;
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: {Conversations} conversas de demonstração.", created);
    }

    private async Task SeedKnowledgeAsync(CancellationToken ct)
    {
        if (await db.KnowledgeDocuments.AnyAsync(ct)) return;

        var companies = await db.Companies.ToListAsync(ct);
        var created = 0;
        foreach (var def in DemoKnowledge.All)
        {
            var name = Companies.First(c => c.Slug == def.CompanySlug).Name;
            var company = companies.FirstOrDefault(c => c.Name == name);
            if (company is null) continue;

            var doc = new KnowledgeDocument
            {
                CompanyId = company.Id,
                Title = def.Title,
                Content = KnowledgeChunker.Normalize(def.Content),
                SourceType = def.Type,
                Status = KnowledgeStatus.Ready,
                CreatedAt = DateTime.UtcNow.AddDays(-40),
                UpdatedAt = DateTime.UtcNow.AddDays(-40)
            };
            db.KnowledgeDocuments.Add(doc);
            db.KnowledgeChunks.AddRange(KnowledgeIndexer.BuildChunks(doc));
            created++;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: {Docs} documentos de conhecimento de demonstração.", created);
    }

    /// <summary>
    /// Senha dos usuários criados pelo seed. A senha padrão (pública, documentada) só é aceita em Development e Testing;
    /// em qualquer outro ambiente, criar usuários sem senha configurada interrompe a inicialização.
    /// </summary>
    private string SeedPassword(string key, string envName, string fallback)
    {
        if (configuration[key] is { Length: > 0 } configured)
        {
            if (configured.Length < 8) throw new InvalidOperationException($"{envName} deve ter ao menos 8 caracteres.");
            return configured;
        }
        if (hostEnvironment.IsDevelopment() || hostEnvironment.IsEnvironment("Testing")) return fallback;
        throw new InvalidOperationException(
            $"Defina {envName} no arquivo .env: fora do ambiente de desenvolvimento, o seed não cria usuários com a senha padrão pública.");
    }

    private async Task SeedCompaniesAndUsersAsync(CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct)) return;

        var adminPassword = SeedPassword("Seed:AdminPassword", "SEED_ADMIN_PASSWORD", DefaultAdminPassword);
        var demoPassword = SeedPassword("Seed:DemoPassword", "SEED_DEMO_PASSWORD", DefaultDemoPassword);
        var demoHash = hasher.Hash(demoPassword);

        db.Users.Add(new User
        {
            Name = "Administrador K2", Email = DefaultAdminEmail, RoleId = UserRole.SuperAdmin,
            PasswordHash = hasher.Hash(adminPassword)
        });

        foreach (var c in Companies)
        {
            var company = new Company
            {
                Name = c.Name, Document = c.Document, Industry = c.Industry, Phone = c.Phone, Website = c.Website,
                Email = $"contato@{c.Slug}.demo", ConversationRetentionDays = 365,
                CreatedAt = DateTime.UtcNow.AddDays(-60)
            };
            db.Companies.Add(company);

            db.Users.Add(NewUser(company, $"Admin {c.Name}", $"admin@{c.Slug}.demo", UserRole.CompanyAdmin, demoHash));
            db.Users.Add(NewUser(company, $"Atendente {c.Name}", $"atendente@{c.Slug}.demo", UserRole.Attendant, demoHash));
            if (c.Slug == "padaria")
            {
                db.Users.Add(NewUser(company, "Gestor de Agentes Padaria", "gestor@padaria.demo", UserRole.AgentManager, demoHash));
                db.Users.Add(NewUser(company, "Consulta Padaria", "consulta@padaria.demo", UserRole.Viewer, demoHash));
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: {Companies} empresas e usuários de demonstração.", Companies.Length);
    }

    private async Task SeedAgentsAsync(CancellationToken ct)
    {
        if (await db.AIAgents.AnyAsync(ct)) return;

        var companies = await db.Companies.ToListAsync(ct);
        var created = 0;
        foreach (var def in DemoAgents.All)
        {
            var demo = Companies.First(c => c.Slug == def.CompanySlug);
            var company = companies.FirstOrDefault(c => c.Name == demo.Name);
            if (company is null) continue;

            var agent = new AIAgent
            {
                CompanyId = company.Id,
                Name = def.Name,
                Description = def.Description,
                BusinessType = def.Type,
                ResponseLanguage = "pt-BR",
                BehaviorJson = AgentService.SerializeBehavior(def.Behavior),
                SystemPrompt = promptBuilder.Build(def.Name, company.Name, def.Type, "pt-BR", def.Behavior),
                WelcomeMessage = def.Welcome,
                Model = models.Default.Id,
                Temperature = 0.4m,
                MaxOutputTokens = 600,
                Status = AgentStatus.Active,
                CreatedAt = DateTime.UtcNow.AddDays(-45),
                UpdatedAt = DateTime.UtcNow.AddDays(-45)
            };
            db.AIAgents.Add(agent);
            db.AgentVersions.Add(new AgentVersion
            {
                CompanyId = company.Id, AgentId = agent.Id, VersionNumber = 1, SystemPrompt = agent.SystemPrompt,
                BehaviorJson = agent.BehaviorJson, Model = agent.Model, Temperature = agent.Temperature,
                MaxOutputTokens = agent.MaxOutputTokens, WelcomeMessage = agent.WelcomeMessage,
                ChangeNote = "Versão inicial (demonstração)", CreatedAt = agent.CreatedAt
            });
            created++;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: {Agents} agentes de demonstração.", created);
    }

    private static User NewUser(Company company, string name, string email, UserRole role, string hash) =>
        new() { CompanyId = company.Id, Name = name, Email = email, RoleId = role, PasswordHash = hash };
}
