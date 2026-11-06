using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using K2Agents.Application.Abstractions;
using K2Agents.Domain.Enums;
using K2Agents.Tests.Support;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Tests.Api;

public class HandoffTests(AIEnabledFactory factory) : IClassFixture<AIEnabledFactory>
{
    private async Task<(HttpClient Visitor, string Token, string SessionId, string Key)> NewWebChatAsync()
    {
        var visitor = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Ana");
        var (sessionId, key, _) = await visitor.StartWebChatAsync(token);
        return (visitor, token, sessionId, key);
    }

    private async Task<ConversationStatus> StatusAsync(string sessionId)
    {
        await using var db = factory.SystemDb();
        return (await db.Conversations.FirstAsync(c => c.Id == Guid.Parse(sessionId))).Status;
    }

    [Fact]
    public async Task CustomerAsksForHuman_TransfersNotifiesAndAudits()
    {
        var (visitor, token, sessionId, key) = await NewWebChatAsync();

        await visitor.SendWebChatAsync(token, sessionId, key, "Quero falar com um atendente");

        Assert.Equal(ConversationStatus.WaitingHuman, await StatusAsync(sessionId));
        await using var db = factory.SystemDb();
        var id = Guid.Parse(sessionId);
        Assert.True(await db.ConversationMessages.AnyAsync(m => m.ConversationId == id && m.Content == "Conversa transferida para atendimento humano."));
        Assert.True(await db.Notifications.AnyAsync(n => n.Type == NotificationType.Handoff && n.Link!.Contains(sessionId)));
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "conversation.handoff_requested" && a.EntityId == sessionId && a.Details == "Solicitado pelo cliente"));
    }

    [Fact]
    public async Task ModelMarker_IsStrippedAndTriggersTransfer()
    {
        var (visitor, token, sessionId, key) = await NewWebChatAsync();
        factory.AI.Enqueue(_ => new AICompletionResult("Reclamações são tratadas pela equipe. Vou transferir você.\n[[TRANSFERIR]]", 50, 20, "stop"));

        var reply = (await (await visitor.SendWebChatAsync(token, sessionId, key, "O bolo veio estragado!")).ReadJsonAsync()).GetProperty("reply");

        Assert.DoesNotContain("[[", reply.GetProperty("content").GetString());
        Assert.Equal(ConversationStatus.WaitingHuman, await StatusAsync(sessionId));
        await using var db = factory.SystemDb();
        Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityId == sessionId && a.Details == "Solicitado pelo agente de IA"));
    }

    [Fact]
    public async Task FullHandoffCycle_ClaimReplyAndReturnToAI()
    {
        var (visitor, token, sessionId, key) = await NewWebChatAsync();
        await visitor.SendWebChatAsync(token, sessionId, key, "Quero falar com um atendente");
        var attendant = await factory.AuthenticatedAsync(Demo.PadariaAttendant);

        // Enquanto aguarda humano, a IA não responde.
        var calls = factory.AI.Requests.Count;
        var waiting = await (await visitor.SendWebChatAsync(token, sessionId, key, "Alô?")).ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, waiting.GetProperty("reply").ValueKind);
        Assert.Equal(calls, factory.AI.Requests.Count);

        // Assumir
        var claimed = await (await attendant.PostAsync($"/api/v1/conversations/{sessionId}/claim", null)).ReadJsonAsync();
        Assert.Equal("HumanActive", claimed.GetProperty("status").GetString());
        Assert.Equal("Atendente Padaria Jardim Andrade", claimed.GetProperty("assignedUserName").GetString());

        // Responder pelo painel → visitante recebe
        var reply = await attendant.PostJsonAsync($"/api/v1/conversations/{sessionId}/messages", new { content = "Olá! Sou a Júlia, como posso ajudar?" });
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        var visitorView = await (await visitor.PublicPostAsync("messages/list", new { token, sessionId, visitorKey = key })).ReadJsonAsync();
        Assert.Contains(visitorView.EnumerateArray(), m => m.GetProperty("senderType").GetString() == "Human"
                                                           && m.GetProperty("content").GetString() == "Olá! Sou a Júlia, como posso ajudar?");

        // Devolver para a IA → IA volta a responder
        var returned = await (await attendant.PostAsync($"/api/v1/conversations/{sessionId}/return-to-ai", null)).ReadJsonAsync();
        Assert.Equal("Open", returned.GetProperty("status").GetString());
        var aiAgain = await (await visitor.SendWebChatAsync(token, sessionId, key, "Qual o horário?")).ReadJsonAsync();
        Assert.Equal("AI", aiAgain.GetProperty("reply").GetProperty("senderType").GetString());

        // Trilha de auditoria completa
        var events = await (await attendant.GetAsync($"/api/v1/conversations/{sessionId}/events")).ReadJsonAsync();
        var actions = events.EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.Equal(["conversation.handoff_requested", "conversation.claimed", "conversation.returned_to_ai"], actions);
    }

    [Fact]
    public async Task ReplyingWithoutClaim_AutoClaims_AndManualTransferWorks()
    {
        var (_, _, sessionId, _) = await NewWebChatAsync();
        var admin = await factory.AuthenticatedAsync(Demo.PadariaAdmin);

        var transferred = await (await admin.PostAsync($"/api/v1/conversations/{sessionId}/transfer", null)).ReadJsonAsync();
        Assert.Equal("WaitingHuman", transferred.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/v1/conversations/{sessionId}/transfer", null)).StatusCode);

        await admin.PostJsonAsync($"/api/v1/conversations/{sessionId}/messages", new { content = "Oi, assumindo aqui." });
        Assert.Equal(ConversationStatus.HumanActive, await StatusAsync(sessionId));
    }

    [Fact]
    public async Task Permissions_And_ClosedConversation()
    {
        var (_, _, sessionId, _) = await NewWebChatAsync();

        var viewer = await factory.AuthenticatedAsync(Demo.PadariaViewer);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostJsonAsync($"/api/v1/conversations/{sessionId}/messages", new { content = "x" })).StatusCode);

        var bella = await factory.AuthenticatedAsync(Demo.BellaAdmin);
        Assert.Equal(HttpStatusCode.NotFound, (await bella.PostAsync($"/api/v1/conversations/{sessionId}/claim", null)).StatusCode);

        var attendant = await factory.AuthenticatedAsync(Demo.PadariaAttendant);
        await attendant.PostAsync($"/api/v1/conversations/{sessionId}/close", null);
        var closed = await attendant.PostJsonAsync($"/api/v1/conversations/{sessionId}/messages", new { content = "Oi" });
        Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
    }

    [Fact]
    public async Task HandoffNotification_IsPushedInRealTime()
    {
        var login = await (await factory.CreateClient().LoginAsync(Demo.PadariaAttendant, Demo.Password)).ReadJsonAsync();
        var jwt = login.GetProperty("accessToken").GetString();
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/inbox"), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult(jwt);
                o.Transports = HttpTransportType.LongPolling;
            }).Build();
        var got = new TaskCompletionSource<JsonElement>();
        hub.On<JsonElement>("notification", n => { if (n.GetProperty("type").GetString() == "Handoff") got.TrySetResult(n); });
        await hub.StartAsync();

        var (visitor, token, sessionId, key) = await NewWebChatAsync();
        await visitor.SendWebChatAsync(token, sessionId, key, "Preciso de atendimento humano");

        var notification = await got.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains(sessionId, notification.GetProperty("link").GetString());
        await hub.DisposeAsync();
    }
}

public class ContactCaptureTests(AIEnabledFactory factory) : IClassFixture<AIEnabledFactory>
{
    [Fact]
    public async Task CustomerData_CreatesLinkedLead_AndNotifiesTeam()
    {
        var visitor = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Ana");
        var (sessionId, key, _) = await visitor.StartWebChatAsync(token);

        await visitor.SendWebChatAsync(token, sessionId, key, "Meu nome é João Pedro, meu e-mail é joao.pedro@cliente.demo");

        await using var db = factory.SystemDb();
        var conversation = await db.Conversations.Include(c => c.Contact).FirstAsync(c => c.Id == Guid.Parse(sessionId));
        Assert.NotNull(conversation.Contact);
        Assert.Equal("João Pedro", conversation.Contact!.Name);
        Assert.Equal("joao.pedro@cliente.demo", conversation.Contact.Email);
        Assert.True(conversation.Contact.IsLead);
        Assert.Equal(ContactStatus.New, conversation.Contact.Status);
        Assert.True(await db.Notifications.AnyAsync(n => n.Type == NotificationType.NewLead && n.Title.Contains("João Pedro")));
    }

    [Fact]
    public async Task SameEmailInNewConversation_ReusesContact_AndPhoneIsAdded()
    {
        var visitor = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Ana");
        var (s1, k1, _) = await visitor.StartWebChatAsync(token);
        await visitor.SendWebChatAsync(token, s1, k1, "meu email é recorrente@cliente.demo");
        var (s2, k2, _) = await factory.CreateClient().StartWebChatAsync(token);
        await visitor.SendWebChatAsync(token, s2, k2, "Oi de novo, recorrente@cliente.demo, telefone 11 97777-6666");

        await using var db = factory.SystemDb();
        var contacts = await db.Contacts.Where(c => c.Email == "recorrente@cliente.demo").ToListAsync();
        var contact = Assert.Single(contacts);
        Assert.Equal("11977776666", contact.Phone);
        Assert.Equal(2, await db.Conversations.CountAsync(c => c.ContactId == contact.Id));
    }

    [Fact]
    public async Task EmailWrittenByTheAI_NeverBecomesAContact()
    {
        var visitor = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Ana");
        var (sessionId, key, _) = await visitor.StartWebChatAsync(token);
        factory.AI.Enqueue(_ => new AICompletionResult("Escreva para inventado@ia.demo ou ligue (11) 91234-5678.", 10, 10, "stop"));

        await visitor.SendWebChatAsync(token, sessionId, key, "Qual o contato de vocês?");

        await using var db = factory.SystemDb();
        Assert.False(await db.Contacts.AnyAsync(c => c.Email == "inventado@ia.demo" || c.Phone == "11912345678"));
        Assert.Null((await db.Conversations.FirstAsync(c => c.Id == Guid.Parse(sessionId))).ContactId);
    }
}

public class ContactsApiTests(K2ApiFactory factory) : IClassFixture<K2ApiFactory>
{
    [Fact]
    public async Task ListAndFunnel_ReflectDemoData_AndAreTenantIsolated()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var list = await (await client.GetAsync("/api/v1/contacts?pageSize=100")).ReadJsonAsync();
        Assert.Contains(list.GetProperty("items").EnumerateArray(), c => c.GetProperty("name").GetString() == "Mariana Alves");
        Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), c => c.GetProperty("name").GetString() == "Juliana Rocha");

        var funnel = await (await client.GetAsync("/api/v1/contacts/funnel")).ReadJsonAsync();
        Assert.Equal(5, funnel.GetProperty("stages").GetArrayLength());
        Assert.True(funnel.GetProperty("total").GetInt32() >= 4);

        var leads = await (await client.GetAsync("/api/v1/contacts?isLead=true&status=Converted")).ReadJsonAsync();
        Assert.All(leads.GetProperty("items").EnumerateArray(), c => Assert.Equal("Converted", c.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Detail_IncludesLinkedConversations()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var mariana = (await (await client.GetAsync("/api/v1/contacts?search=mariana")).ReadJsonAsync()).GetProperty("items")[0];
        var detail = await (await client.GetAsync($"/api/v1/contacts/{mariana.GetProperty("id").GetString()}")).ReadJsonAsync();
        Assert.True(detail.GetProperty("conversations").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Create_NormalizesPhone_RejectsDuplicates()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAttendant);
        var created = await client.PostJsonAsync("/api/v1/contacts", new { name = "Novo Cliente", email = "Novo@Cliente.demo", phone = "(11) 95555-0000" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.ReadJsonAsync();
        Assert.Equal("novo@cliente.demo", body.GetProperty("email").GetString());
        Assert.Equal("11955550000", body.GetProperty("phone").GetString());

        var duplicate = await client.PostJsonAsync("/api/v1/contacts", new { name = "Outro", phone = "11 95555-0000" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task FunnelProgress_ToQualified_MarksAsLead()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAttendant);
        var contact = await (await client.PostJsonAsync("/api/v1/contacts", new { name = "Prospect", isLead = false })).ReadJsonAsync();
        var updated = await (await client.PatchJsonAsync($"/api/v1/contacts/{contact.GetProperty("id").GetString()}/status", new { status = "Qualified" })).ReadJsonAsync();
        Assert.True(updated.GetProperty("isLead").GetBoolean());
    }

    [Fact]
    public async Task Anonymize_RemovesPersonalDataAndMessages_OnlyForAdmins()
    {
        var admin = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var contactId = (await (await admin.GetAsync("/api/v1/contacts?search=carlos")).ReadJsonAsync()).GetProperty("items")[0].GetProperty("id").GetString()!;
        await using (var db = factory.SystemDb())
        {
            var conversation = await db.Conversations.FirstAsync(c => c.IsDemo && c.CompanyId == db.Contacts.First(x => x.Id == Guid.Parse(contactId)).CompanyId);
            conversation.ContactId = Guid.Parse(contactId);
            await db.SaveChangesAsync();
        }

        var attendant = await factory.AuthenticatedAsync(Demo.PadariaAttendant);
        Assert.Equal(HttpStatusCode.Forbidden, (await attendant.PostAsync($"/api/v1/contacts/{contactId}/anonymize", null)).StatusCode);

        var result = await (await admin.PostAsync($"/api/v1/contacts/{contactId}/anonymize", null)).ReadJsonAsync();
        Assert.Equal("Contato anonimizado", result.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("email").ValueKind);
        Assert.True(result.GetProperty("isAnonymized").GetBoolean());

        await using var check = factory.SystemDb();
        var ids = await check.Conversations.Where(c => c.ContactId == Guid.Parse(contactId)).Select(c => c.Id).ToListAsync();
        Assert.All(await check.ConversationMessages.Where(m => ids.Contains(m.ConversationId) && m.SenderType == SenderType.Contact).ToListAsync(),
            m => Assert.StartsWith("[mensagem removida", m.Content));
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PutJsonAsync($"/api/v1/contacts/{contactId}", new { name = "Reidentificar" })).StatusCode);
    }

    [Fact]
    public async Task LinkContactFromConversation_CreatesAndLinks()
    {
        var token = await factory.ChannelTokenAsync("Ana");
        var (sessionId, _, _) = await factory.CreateClient().StartWebChatAsync(token);
        var attendant = await factory.AuthenticatedAsync(Demo.PadariaAttendant);

        var detail = await (await attendant.PostJsonAsync($"/api/v1/conversations/{sessionId}/contact",
            new { name = "Cliente Identificado", phone = "11 94444-1111" })).ReadJsonAsync();

        Assert.Equal("Cliente Identificado", detail.GetProperty("contact").GetProperty("name").GetString());
        Assert.Equal("Cliente Identificado", detail.GetProperty("summary").GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Notifications_ListAndMarkRead()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAttendant);
        var list = await (await client.GetAsync("/api/v1/notifications")).ReadJsonAsync();
        Assert.True(list.GetProperty("unread").GetInt32() >= 1); // seed: conversa aguardando humano

        var id = list.GetProperty("items")[0].GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/notifications/{id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/notifications/read-all", null)).StatusCode);
        Assert.Equal(0, (await (await client.GetAsync("/api/v1/notifications")).ReadJsonAsync()).GetProperty("unread").GetInt32());

        var bella = await factory.AuthenticatedAsync(Demo.BellaAdmin);
        Assert.Equal(HttpStatusCode.NotFound, (await bella.PostAsync($"/api/v1/notifications/{id}/read", null)).StatusCode);
    }
}
