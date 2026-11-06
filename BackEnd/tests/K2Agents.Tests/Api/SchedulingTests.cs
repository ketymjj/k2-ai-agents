using System.Net;
using System.Text.Json;
using K2Agents.Application.Abstractions;
using K2Agents.Domain.Enums;
using K2Agents.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Tests.Api;

public static class SchedulingHelpers
{
    public const string BellaAttendant = "atendente@studiobella.demo";

    /// <summary>Primeiro dia (a partir de amanhã) com horários livres para o serviço; retorna os slots.</summary>
    public static async Task<(string Date, JsonElement Slots)> FirstAvailableAsync(this HttpClient client, string service)
    {
        for (var d = 1; d <= 14; d++)
        {
            var date = DateTime.UtcNow.AddDays(d).ToString("yyyy-MM-dd");
            var availability = await (await client.GetAsync($"/api/v1/appointments/availability?date={date}&service={Uri.EscapeDataString(service)}")).ReadJsonAsync();
            if (availability.GetProperty("slots").GetArrayLength() > 2) return (date, availability.GetProperty("slots"));
        }
        throw new InvalidOperationException("Sem horários livres nos próximos 14 dias.");
    }
}

public class AppointmentsTests(K2ApiFactory factory) : IClassFixture<K2ApiFactory>
{
    [Fact]
    public async Task Settings_AreSeeded_AndValidatedOnSave()
    {
        var admin = await factory.AuthenticatedAsync(Demo.BellaAdmin);
        var settings = await (await admin.GetAsync("/api/v1/appointments/settings")).ReadJsonAsync();
        Assert.Contains(settings.GetProperty("services").EnumerateArray(), s => s.GetProperty("name").GetString() == "Escova");

        var invalid = await admin.PutJsonAsync("/api/v1/appointments/settings", new
        {
            timeZone = "America/Sao_Paulo", slotMinutes = 30, minAdvanceMinutes = 60, maxDaysAhead = 30,
            services = new[] { new { name = "Escova", durationMinutes = 45 } },
            weeklyHours = new Dictionary<string, object> { ["monday"] = new[] { new { start = "18:00", end = "09:00" } } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var viewer = await factory.AuthenticatedAsync(Demo.PadariaViewer);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutJsonAsync("/api/v1/appointments/settings", new { })).StatusCode);
    }

    [Fact]
    public async Task Create_RejectsOverlap_AllowsAdjacent_AndCancellationFreesTheSlot()
    {
        var client = await factory.AuthenticatedAsync(SchedulingHelpers.BellaAttendant);
        var (_, slots) = await client.FirstAvailableAsync("Escova"); // 45 min
        // Início com tudo livre até +105 min (s, s+30 e s+60 livres por 45 min cada), para a reserva adjacente
        // (+45, 45 min) não esbarrar num agendamento do seed, que muda de lugar conforme a data atual.
        var starts = slots.EnumerateArray().Select(s => s.GetProperty("start").GetDateTime()).ToList();
        var start = starts.First(s => starts.Contains(s.AddMinutes(30)) && starts.Contains(s.AddMinutes(60)));

        var first = await client.PostJsonAsync("/api/v1/appointments", new { serviceName = "Escova", startDateTime = start });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstId = (await first.ReadJsonAsync()).GetProperty("id").GetString();

        var overlap = await client.PostJsonAsync("/api/v1/appointments", new { serviceName = "Manicure", startDateTime = start.AddMinutes(30) });
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        Assert.Equal("slot_unavailable", (await overlap.ReadJsonAsync()).GetProperty("code").GetString());

        var adjacent = await client.PostJsonAsync("/api/v1/appointments", new { serviceName = "Manicure", startDateTime = start.AddMinutes(45) });
        Assert.Equal(HttpStatusCode.Created, adjacent.StatusCode);

        await client.PatchJsonAsync($"/api/v1/appointments/{firstId}/status", new { status = "Cancelled" });
        var again = await client.PostJsonAsync("/api/v1/appointments", new { serviceName = "Design de sobrancelha", startDateTime = start });
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task Availability_ExcludesBookedSlots()
    {
        var client = await factory.AuthenticatedAsync(SchedulingHelpers.BellaAttendant);
        var (date, slots) = await client.FirstAvailableAsync("Corte feminino");
        var taken = slots[1].GetProperty("start").GetDateTime();
        await client.PostJsonAsync("/api/v1/appointments", new { serviceName = "Corte feminino", startDateTime = taken });

        var after = await (await client.GetAsync($"/api/v1/appointments/availability?date={date}&service=Corte%20feminino")).ReadJsonAsync();
        Assert.DoesNotContain(after.GetProperty("slots").EnumerateArray(), s => s.GetProperty("start").GetDateTime() == taken);
    }

    [Fact]
    public async Task StatusTransitions_AreEnforced_AndRescheduleChecksConflicts()
    {
        var client = await factory.AuthenticatedAsync(SchedulingHelpers.BellaAttendant);
        var (_, slots) = await client.FirstAvailableAsync("Manicure");
        var a = await (await client.PostJsonAsync("/api/v1/appointments", new { serviceName = "Manicure", startDateTime = slots[0].GetProperty("start").GetDateTime() })).ReadJsonAsync();
        var b = await (await client.PostJsonAsync("/api/v1/appointments", new { serviceName = "Manicure", startDateTime = slots[2].GetProperty("start").GetDateTime() })).ReadJsonAsync();
        var aId = a.GetProperty("id").GetString();

        Assert.Equal("Confirmed", (await (await client.PatchJsonAsync($"/api/v1/appointments/{aId}/status", new { status = "Confirmed" })).ReadJsonAsync()).GetProperty("status").GetString());

        var clash = await client.PostJsonAsync($"/api/v1/appointments/{aId}/reschedule", new { startDateTime = b.GetProperty("startDateTime").GetDateTime() });
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);

        await client.PatchJsonAsync($"/api/v1/appointments/{aId}/status", new { status = "Completed" });
        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchJsonAsync($"/api/v1/appointments/{aId}/status", new { status = "Scheduled" })).StatusCode);
    }

    [Fact]
    public async Task Creation_TriggersInternalConfirmationAutomation()
    {
        var client = await factory.AuthenticatedAsync(SchedulingHelpers.BellaAttendant);
        var (_, slots) = await client.FirstAvailableAsync("Design de sobrancelha");
        await client.PostJsonAsync("/api/v1/appointments", new { serviceName = "Design de sobrancelha", startDateTime = slots.EnumerateArray().Last().GetProperty("start").GetDateTime() });

        await using var db = factory.SystemDb();
        Assert.True(await db.Notifications.AnyAsync(n => n.Type == NotificationType.Appointment && n.Title.Contains("Design de sobrancelha")));
    }

    [Fact]
    public async Task Tenancy_And_Roles()
    {
        var bella = await factory.AuthenticatedAsync(SchedulingHelpers.BellaAttendant);
        var (_, slots) = await bella.FirstAvailableAsync("Escova");
        var created = await (await bella.PostJsonAsync("/api/v1/appointments", new { serviceName = "Escova", startDateTime = slots.EnumerateArray().Last().GetProperty("start").GetDateTime() })).ReadJsonAsync();
        var id = created.GetProperty("id").GetString();

        var padaria = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        Assert.Equal(HttpStatusCode.NotFound, (await padaria.GetAsync($"/api/v1/appointments/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await padaria.PatchJsonAsync($"/api/v1/appointments/{id}/status", new { status = "Cancelled" })).StatusCode);
        var list = await (await padaria.GetAsync("/api/v1/appointments")).ReadJsonAsync();
        Assert.DoesNotContain(list.EnumerateArray(), a => a.GetProperty("id").GetString() == id);

        var viewer = await factory.AuthenticatedAsync(Demo.PadariaViewer);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostJsonAsync("/api/v1/appointments", new { serviceName = "X", startDateTime = DateTime.UtcNow })).StatusCode);
    }
}

public class AutomationsTests(K2ApiFactory factory) : IClassFixture<K2ApiFactory>
{
    [Fact]
    public async Task Catalog_AndDefaultRules()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var catalog = await (await client.GetAsync("/api/v1/automations/catalog")).ReadJsonAsync();
        Assert.Contains("contato", catalog.GetProperty("placeholders").EnumerateArray().Select(p => p.GetString()));

        var rules = await (await client.GetAsync("/api/v1/automations")).ReadJsonAsync();
        Assert.Equal(4, rules.GetArrayLength());
    }

    [Fact]
    public async Task InvalidTriggerActionCombination_IsRejected()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var response = await client.PostJsonAsync("/api/v1/automations", new
        {
            name = "Inválida", triggerType = "ConversationClosed", actionType = "CreateInternalConfirmation"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DisabledRule_DoesNotRun_CustomRuleUsesTemplate()
    {
        var admin = await factory.AuthenticatedAsync("admin@horizonte.demo");
        var rules = await (await admin.GetAsync("/api/v1/automations")).ReadJsonAsync();
        var leadRule = rules.EnumerateArray().First(r => r.GetProperty("triggerType").GetString() == "LeadCreated");
        await admin.PatchJsonAsync($"/api/v1/automations/{leadRule.GetProperty("id").GetString()}/enabled", new { enabled = false });

        var custom = await admin.PostJsonAsync("/api/v1/automations", new
        {
            name = "Lead VIP", triggerType = "LeadCreated", actionType = "CreateNotification",
            configuration = new { title = "VIP: {contato} ({canal}) {inexistente}", message = "Ligar em até 1h." }
        });
        Assert.Equal(HttpStatusCode.Created, custom.StatusCode);

        await admin.PostJsonAsync("/api/v1/contacts", new { name = "Cliente Premium", email = "premium@cliente.demo", isLead = true });

        await using var db = factory.SystemDb();
        var titles = await db.Notifications.Where(n => n.Title.Contains("Cliente Premium")).Select(n => n.Title).ToListAsync();
        Assert.Equal(["VIP: Cliente Premium (Manual) {inexistente}"], titles); // regra padrão desligada não gerou "Novo lead: ..."
        var custRule = await db.AutomationRules.FirstAsync(r => r.Name == "Lead VIP");
        Assert.Equal(1, custRule.ExecutionCount);
    }

    [Fact]
    public async Task OnlyAdminsChangeRules()
    {
        var attendant = await factory.AuthenticatedAsync(Demo.PadariaAttendant);
        Assert.Equal(HttpStatusCode.OK, (await attendant.GetAsync("/api/v1/automations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await attendant.PostJsonAsync("/api/v1/automations", new { name = "x", triggerType = "LeadCreated", actionType = "RecordMetric" })).StatusCode);
    }

    [Fact]
    public async Task NewCompany_GetsDefaultRules()
    {
        var admin = await factory.AuthenticatedAsync(Demo.AdminEmail, Demo.AdminPassword);
        var company = await (await admin.PostJsonAsync("/api/v1/companies", new { name = "Empresa com automações" })).ReadJsonAsync();
        var rules = await (await admin.GetAsync($"/api/v1/automations?companyId={company.GetProperty("id").GetString()}")).ReadJsonAsync();
        Assert.Equal(4, rules.GetArrayLength());
    }
}

/// <summary>Agente de agendamento usando function calling (provedor de IA simulado nos testes).</summary>
public class AgentSchedulingToolsTests(AIEnabledFactory factory) : IClassFixture<AIEnabledFactory>
{
    private static AICompletionResult ToolCall(string name, object args) =>
        new(string.Empty, 200, 30, "tool_calls", [new AIToolCall("call_" + Guid.NewGuid().ToString("N")[..8], name, JsonSerializer.Serialize(args))]);

    private async Task<(string Date, string Time)> FreeSlotAsync(string service)
    {
        var staff = await factory.AuthenticatedAsync(SchedulingHelpers.BellaAttendant);
        var (date, slots) = await staff.FirstAvailableAsync(service);
        var label = slots.EnumerateArray().Last().GetProperty("localLabel").GetString()!; // "dd/MM HH:mm"
        return (date, label[6..]);
    }

    [Fact]
    public async Task SchedulingAgent_ReceivesTools_AndAvailabilityComesFromTheRealSchedule()
    {
        var (date, _) = await FreeSlotAsync("Escova");
        factory.AI.Enqueue(_ => ToolCall("check_availability", new { service = "Escova", date }));
        factory.AI.Enqueue(_ => new AICompletionResult("Tenho horários livres nesse dia.", 300, 20, "stop"));

        var client = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Bella");
        var (sessionId, key, _) = await client.StartWebChatAsync(token);
        var reply = await (await client.SendWebChatAsync(token, sessionId, key, $"Tem horário para escova em {date}?")).ReadJsonAsync();

        Assert.Equal("Tenho horários livres nesse dia.", reply.GetProperty("reply").GetProperty("content").GetString());
        var second = factory.AI.Requests.Last(r => r.Messages.Any(m => m.Role == AIRole.Tool));
        Assert.NotNull(second.Tools);
        Assert.Contains(second.Tools!, t => t.Name == "book_appointment");
        var toolResult = second.Messages.Last(m => m.Role == AIRole.Tool).Content!;
        Assert.Contains("\"ok\":true", toolResult);
        Assert.Contains("available_times", toolResult);
        Assert.Contains("Agenda real", second.SystemPrompt);
    }

    [Fact]
    public async Task Booking_WithCustomerProvidedName_CreatesAppointmentAndContact()
    {
        var (date, time) = await FreeSlotAsync("Manicure");
        factory.AI.Enqueue(_ => ToolCall("book_appointment", new { service = "Manicure", date, time, customer_name = "Renata Prado" }));
        factory.AI.Enqueue(_ => new AICompletionResult("Pronto, Renata! Agendado.", 300, 20, "stop"));

        var client = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Bella");
        var (sessionId, key, _) = await client.StartWebChatAsync(token);
        await client.SendWebChatAsync(token, sessionId, key, $"Meu nome é Renata Prado, quero manicure dia {date} às {time}");

        await using var db = factory.SystemDb();
        var appointment = await db.Appointments.Include(a => a.Contact).SingleAsync(a => a.ConversationId == Guid.Parse(sessionId));
        Assert.Equal("Agent", appointment.Source);
        Assert.Equal("Renata Prado", appointment.Contact!.Name);
        Assert.Equal(appointment.ContactId, (await db.Conversations.FirstAsync(c => c.Id == Guid.Parse(sessionId))).ContactId);
    }

    [Fact]
    public async Task Booking_WithInventedName_IsRefusedByTheServer()
    {
        var (date, time) = await FreeSlotAsync("Escova");
        factory.AI.Enqueue(_ => ToolCall("book_appointment", new { service = "Escova", date, time, customer_name = "Nome Inventado" }));
        factory.AI.Enqueue(r => new AICompletionResult("Qual é o seu nome?", 100, 10, "stop"));

        var client = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Bella");
        var (sessionId, key, _) = await client.StartWebChatAsync(token);
        await client.SendWebChatAsync(token, sessionId, key, $"Quero escova dia {date} às {time}");

        var toolResult = factory.AI.Requests.Last(r => r.Messages.Any(m => m.Role == AIRole.Tool)).Messages.Last(m => m.Role == AIRole.Tool).Content!;
        Assert.Contains("\"ok\":false", toolResult);
        Assert.Contains("nome", toolResult);
        await using var db = factory.SystemDb();
        Assert.False(await db.Appointments.AnyAsync(a => a.ConversationId == Guid.Parse(sessionId)));
    }

    [Fact]
    public async Task Booking_InPlayground_IsADryRun()
    {
        var (date, time) = await FreeSlotAsync("Escova");
        factory.AI.Enqueue(_ => ToolCall("book_appointment", new { service = "Escova", date, time, customer_name = "Teste Silva" }));
        factory.AI.Enqueue(_ => new AICompletionResult("Seria agendado.", 100, 10, "stop"));

        var admin = await factory.AuthenticatedAsync(Demo.BellaAdmin);
        var sessionId = (await admin.StartSessionAsync(await factory.AgentIdAsync("Bella"))).GetProperty("id").GetString()!;
        await admin.SendAsync(sessionId, "Meu nome é Teste Silva");

        var toolResult = factory.AI.Requests.Last(r => r.Messages.Any(m => m.Role == AIRole.Tool)).Messages.Last(m => m.Role == AIRole.Tool).Content!;
        Assert.Contains("\"dry_run\":true", toolResult);
        await using var db = factory.SystemDb();
        Assert.False(await db.Appointments.AnyAsync(a => a.ConversationId == Guid.Parse(sessionId)));
    }

    [Fact]
    public async Task NonSchedulingAgent_GetsNoTools()
    {
        var admin = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var sessionId = (await admin.StartSessionAsync(await factory.AgentIdAsync("Ana"))).GetProperty("id").GetString()!;
        await admin.SendAsync(sessionId, "Oi, mensagem sem ferramentas");
        Assert.Null(factory.AI.Requests.Last(r => r.Messages[^1].Content == "Oi, mensagem sem ferramentas").Tools);
    }

    [Fact]
    public async Task EndlessToolCalls_StopAfterLimit_AndHandOff()
    {
        var (date, _) = await FreeSlotAsync("Escova");
        for (var i = 0; i < 6; i++) factory.AI.Enqueue(_ => ToolCall("check_availability", new { service = "Escova", date }));

        var client = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Bella");
        var (sessionId, key, _) = await client.StartWebChatAsync(token);
        await client.SendWebChatAsync(token, sessionId, key, "Loop de ferramentas");

        await using var db = factory.SystemDb();
        Assert.Equal(ConversationStatus.WaitingHuman, (await db.Conversations.FirstAsync(c => c.Id == Guid.Parse(sessionId))).Status);
        Assert.Equal(AgentOrchestratorLimit, await db.AIUsageRecords.CountAsync(u => u.ConversationId == Guid.Parse(sessionId)));
        factory.AI.Clear(); // respostas programadas que sobraram não podem vazar para outros testes
    }

    private const int AgentOrchestratorLimit = Application.Conversations.AgentOrchestrator.MaxToolRounds;
}

public class SimulatedSchedulingTests(K2ApiFactory factory) : IClassFixture<K2ApiFactory>
{
    [Fact]
    public async Task OffersRealSlots_AsksName_ThenBooksTheChosenOne()
    {
        var client = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Bella");
        var (sessionId, key, _) = await client.StartWebChatAsync(token);

        var offer = (await (await client.SendWebChatAsync(token, sessionId, key, "Quero agendar uma escova")).ReadJsonAsync()).GetProperty("reply");
        Assert.True(offer.GetProperty("isSimulated").GetBoolean());
        var offerText = offer.GetProperty("content").GetString()!;
        Assert.Contains("Escova", offerText);
        Assert.Contains("1) ", offerText);

        var askName = (await (await client.SendWebChatAsync(token, sessionId, key, "1")).ReadJsonAsync()).GetProperty("reply").GetProperty("content").GetString()!;
        Assert.Contains("nome", askName);

        await client.SendWebChatAsync(token, sessionId, key, "Meu nome é Carla Souza");
        var booked = (await (await client.SendWebChatAsync(token, sessionId, key, "1")).ReadJsonAsync()).GetProperty("reply").GetProperty("content").GetString()!;
        Assert.StartsWith("Agendado!", booked);

        await using var db = factory.SystemDb();
        var appointment = await db.Appointments.Include(a => a.Contact).SingleAsync(a => a.ConversationId == Guid.Parse(sessionId));
        Assert.Equal("Escova", appointment.ServiceName);
        Assert.Equal("Carla Souza", appointment.Contact!.Name);
    }

    [Fact]
    public async Task OpeningHoursQuestion_IsNotTreatedAsBooking()
    {
        var client = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Bella");
        var (sessionId, key, _) = await client.StartWebChatAsync(token);
        var reply = (await (await client.SendWebChatAsync(token, sessionId, key, "Qual o horário de vocês?")).ReadJsonAsync()).GetProperty("reply").GetProperty("content").GetString()!;
        Assert.DoesNotContain("Responda com o número", reply);
    }
}
