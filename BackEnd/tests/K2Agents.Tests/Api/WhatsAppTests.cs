using System.Net;
using System.Text.Json;
using K2Agents.Domain.Enums;
using K2Agents.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Tests.Api;

public class WhatsAppTests(AIEnabledFactory factory) : IClassFixture<AIEnabledFactory>
{
    private const string AppSecret = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6";
    private const string AccessToken = "EAAG-token-de-teste-nao-real-123";

    private async Task<(HttpClient Admin, string ChannelId)> ActiveChannelAsync(string phoneNumberId)
    {
        var admin = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var agents = await (await admin.GetAsync("/api/v1/agents?status=Active")).ReadJsonAsync();
        var agentId = agents.GetProperty("items")[0].GetProperty("id").GetString();
        var created = await admin.PostJsonAsync("/api/v1/channels/whatsapp", new
        {
            agentId, name = "WhatsApp Padaria", phoneNumberId, accessToken = AccessToken, appSecret = AppSecret
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.ReadJsonAsync()).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchJsonAsync($"/api/v1/channels/{id}/status", new { status = "Active" })).StatusCode);
        return (admin, id);
    }

    private async Task<T> EventuallyAsync<T>(Func<Task<T>> probe, Func<T, bool> done)
    {
        for (var i = 0; i < 50; i++)
        {
            var value = await probe();
            if (done(value)) return value;
            await Task.Delay(100);
        }
        return await probe();
    }

    private Task<HttpResponseMessage> PostWebhookAsync(string body, string? secret = AppSecret) =>
        factory.CreateClient().SendAsync(WhatsAppWebhook.Request(body, secret is null ? null : WhatsAppWebhook.Sign(secret, body)));

    [Fact]
    public async Task Channel_StoresCredentialsEncrypted_AndNeverReturnsThem()
    {
        var admin = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var agents = await (await admin.GetAsync("/api/v1/agents")).ReadJsonAsync();
        var agentId = agents.GetProperty("items")[0].GetProperty("id").GetString();

        var pending = await (await admin.PostJsonAsync("/api/v1/channels/whatsapp", new { agentId, name = "WA", phoneNumberId = "1000001" })).ReadJsonAsync();
        Assert.Equal("PendingCredentials", pending.GetProperty("status").GetString());
        var id = pending.GetProperty("id").GetString();
        var activate = await admin.PatchJsonAsync($"/api/v1/channels/{id}/status", new { status = "Active" });
        Assert.Equal(HttpStatusCode.Conflict, activate.StatusCode);

        var updated = await admin.PutJsonAsync($"/api/v1/channels/{id}/whatsapp", new { agentId, name = "WA", phoneNumberId = "1000001", accessToken = AccessToken, appSecret = AppSecret });
        var raw = await updated.Content.ReadAsStringAsync();
        Assert.DoesNotContain(AccessToken, raw);
        Assert.DoesNotContain(AppSecret, raw);
        var dto = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("Inactive", dto.GetProperty("status").GetString());
        Assert.True(dto.GetProperty("whatsApp").GetProperty("hasAccessToken").GetBoolean());
        Assert.StartsWith("k2v_", dto.GetProperty("whatsApp").GetProperty("verifyToken").GetString());

        // No banco: só o texto cifrado.
        await using var db = factory.SystemDb();
        var stored = await db.ChannelConfigurations.IgnoreQueryFilters().FirstAsync(c => c.Id == Guid.Parse(id!));
        Assert.StartsWith("v1:", stored.EncryptedSecrets);
        Assert.DoesNotContain(AccessToken, stored.EncryptedSecrets);

        // Salvar sem credenciais mantém as atuais.
        var kept = await (await admin.PutJsonAsync($"/api/v1/channels/{id}/whatsapp", new { agentId, name = "WA 2", phoneNumberId = "1000001" })).ReadJsonAsync();
        Assert.True(kept.GetProperty("whatsApp").GetProperty("hasAppSecret").GetBoolean());

        var test = await (await admin.PostAsync($"/api/v1/channels/{id}/whatsapp/test", null)).ReadJsonAsync();
        Assert.True(test.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task OnlyCompanyAdmins_ConfigureWhatsApp_AndANumberBelongsToOneChannel()
    {
        var manager = await factory.AuthenticatedAsync("gestor@padaria.demo");
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostJsonAsync("/api/v1/channels/whatsapp", new { name = "x", phoneNumberId = "123456" })).StatusCode);

        await ActiveChannelAsync("2000002");
        var bella = await factory.AuthenticatedAsync(Demo.BellaAdmin);
        var agents = await (await bella.GetAsync("/api/v1/agents")).ReadJsonAsync();
        var duplicate = await bella.PostJsonAsync("/api/v1/channels/whatsapp",
            new { agentId = agents.GetProperty("items")[0].GetProperty("id").GetString(), name = "Outro", phoneNumberId = "2000002" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task WebhookVerification_RequiresTheChannelVerifyToken()
    {
        var (admin, id) = await ActiveChannelAsync("3000003");
        var token = (await (await admin.GetAsync($"/api/v1/channels/{id}")).ReadJsonAsync()).GetProperty("whatsApp").GetProperty("verifyToken").GetString();
        var client = factory.CreateClient();

        var ok = await client.GetAsync($"/api/v1/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token={token}&hub.challenge=12345");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("12345", await ok.Content.ReadAsStringAsync());

        var wrong = await client.GetAsync("/api/v1/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token=errado&hub.challenge=12345");
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
    }

    [Fact]
    public async Task Webhook_WithInvalidSignature_IsRejected_AndNothingIsStored()
    {
        await ActiveChannelAsync("4000004");
        var body = WhatsAppWebhook.TextMessage("4000004", "5511999990001", "wamid.IN-REJECT", "Oi");

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostWebhookAsync(body, secret: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostWebhookAsync(body, secret: "outro-segredo-qualquer-123456")).StatusCode);
        // Corpo alterado depois de assinado.
        var tampered = WhatsAppWebhook.Request(body.Replace("Oi", "Ola"), WhatsAppWebhook.Sign(AppSecret, body));
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(tampered)).StatusCode);

        await Task.Delay(300);
        await using var db = factory.SystemDb();
        Assert.False(await db.ConversationMessages.IgnoreQueryFilters().AnyAsync(m => m.ExternalMessageId == "wamid.IN-REJECT"));
    }

    [Fact]
    public async Task InboundMessage_GetsAnAIReplyThroughTheCloudApi_OnlyOnce()
    {
        await ActiveChannelAsync("5000005");
        factory.AI.Enqueue(_ => new Application.Abstractions.AICompletionResult("Olá! Temos pão francês fresquinho.", 120, 15, "stop"));
        var body = WhatsAppWebhook.TextMessage("5000005", "5511988887777", "wamid.IN-1", "Vocês têm pão francês?", "Marina Lopes");

        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(body)).StatusCode);
        // A Meta reenvia o mesmo webhook: não pode duplicar.
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(body)).StatusCode);

        var sent = await EventuallyAsync(() => Task.FromResult(factory.WhatsApp.Messages.Where(m => m.To == "5511988887777").ToList()), l => l.Count > 0);
        Assert.Single(sent);
        Assert.Equal("5000005", sent[0].PhoneNumberId);
        Assert.Equal(AccessToken, sent[0].AccessToken); // decifrado só no momento do envio
        Assert.Contains("pão francês", sent[0].Text);

        await Task.Delay(300);
        await using var db = factory.SystemDb();
        var conversation = await db.Conversations.IgnoreQueryFilters().Include(c => c.Contact).SingleAsync(c => c.ExternalSessionId == "wa:5511988887777");
        Assert.Equal(ChannelType.WhatsApp, conversation.Channel);
        Assert.Equal("Marina Lopes", conversation.Contact!.Name);
        Assert.Equal("5511988887777", conversation.Contact.Phone);
        var messages = await db.ConversationMessages.IgnoreQueryFilters().Where(m => m.ConversationId == conversation.Id).OrderBy(m => m.CreatedAt).ToListAsync();
        Assert.Equal(new[] { SenderType.Contact, SenderType.AI }, messages.Select(m => m.SenderType));
        Assert.Equal("wamid.IN-1", messages[0].ExternalMessageId);
        Assert.StartsWith("wamid.TEST", messages[1].ExternalMessageId);
    }

    [Fact]
    public async Task HumanReply_IsDeliveredByWhatsApp_OnlyWithinThe24HourWindow()
    {
        var (admin, _) = await ActiveChannelAsync("6000006");
        await PostWebhookAsync(WhatsAppWebhook.TextMessage("6000006", "5511977776666", "wamid.IN-H1", "Quero falar com um atendente"));
        var conversationId = await EventuallyAsync(async () =>
        {
            await using var db = factory.SystemDb();
            return await db.Conversations.IgnoreQueryFilters().Where(c => c.ExternalSessionId == "wa:5511977776666").Select(c => (Guid?)c.Id).FirstOrDefaultAsync();
        }, id => id is not null);
        await Task.Delay(300);

        var attendant = await factory.AuthenticatedAsync(Demo.PadariaAttendant);
        var reply = await attendant.PostJsonAsync($"/api/v1/conversations/{conversationId}/messages", new { content = "Oi! Sou a Carla, como posso ajudar?" });
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        Assert.Contains(factory.WhatsApp.Messages, m => m.To == "5511977776666" && m.Text.StartsWith("Oi! Sou a Carla"));

        // Fora da janela: a última mensagem do cliente tem mais de 24 h.
        await using (var db = factory.SystemDb())
        {
            foreach (var m in db.ConversationMessages.IgnoreQueryFilters().Where(m => m.ConversationId == conversationId && m.SenderType == SenderType.Contact))
                m.CreatedAt = DateTime.UtcNow.AddHours(-25);
            await db.SaveChangesAsync();
        }
        var late = await attendant.PostJsonAsync($"/api/v1/conversations/{conversationId}/messages", new { content = "Ainda está aí?" });
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("whatsapp_window_closed", (await late.ReadJsonAsync()).GetProperty("code").GetString());
    }
}

/// <summary>Sem IA real configurada, o WhatsApp NÃO responde com texto simulado a um cliente real: vai para a equipe.</summary>
public class WhatsAppWithoutAITests(K2ApiFactory factory) : IClassFixture<K2ApiFactory>
{
    [Fact]
    public async Task WithoutAIKey_TheConversationGoesToTheTeam_AndNothingIsSent()
    {
        var admin = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var agents = await (await admin.GetAsync("/api/v1/agents?status=Active")).ReadJsonAsync();
        const string secret = "f00dbabef00dbabef00dbabef00dbabe";
        var created = await (await admin.PostJsonAsync("/api/v1/channels/whatsapp", new
        {
            agentId = agents.GetProperty("items")[0].GetProperty("id").GetString(), name = "WA", phoneNumberId = "7000007",
            accessToken = "EAAG-token-de-teste", appSecret = secret
        })).ReadJsonAsync();
        await admin.PatchJsonAsync($"/api/v1/channels/{created.GetProperty("id").GetString()}/status", new { status = "Active" });

        var body = WhatsAppWebhook.TextMessage("7000007", "5511966665555", "wamid.IN-NOAI", "Oi, tudo bem?");
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().SendAsync(WhatsAppWebhook.Request(body, WhatsAppWebhook.Sign(secret, body)))).StatusCode);

        ConversationStatus? status = null;
        for (var i = 0; i < 50 && status != ConversationStatus.WaitingHuman; i++)
        {
            await Task.Delay(100);
            await using var db = factory.SystemDb();
            status = await db.Conversations.IgnoreQueryFilters().Where(c => c.ExternalSessionId == "wa:5511966665555").Select(c => (ConversationStatus?)c.Status).FirstOrDefaultAsync();
        }
        Assert.Equal(ConversationStatus.WaitingHuman, status);
        Assert.DoesNotContain(factory.WhatsApp.Messages, m => m.To == "5511966665555");
    }
}
