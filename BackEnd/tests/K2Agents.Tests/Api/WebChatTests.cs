using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using K2Agents.Application.Abstractions;
using K2Agents.Domain.Enums;
using K2Agents.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Tests.Api;

public static class WebChatHelpers
{
    public static async Task<string> ChannelTokenAsync(this K2ApiFactory factory, string agentName)
    {
        await using var db = factory.SystemDb();
        return (await db.ChannelConfigurations.Include(c => c.Agent).FirstAsync(c => c.Agent!.Name == agentName)).PublicToken!;
    }

    public static Task<HttpResponseMessage> PublicPostAsync(this HttpClient client, string path, object body, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/public/webchat/{path}") { Content = JsonContent.Create(body) };
        if (origin is not null) request.Headers.Add("Origin", origin);
        return client.SendAsync(request);
    }

    public static async Task<(string SessionId, string VisitorKey, JsonElement Body)> StartWebChatAsync(this HttpClient client, string token)
    {
        var response = await client.PublicPostAsync("sessions", new { token });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        return (body.GetProperty("sessionId").GetString()!, body.GetProperty("visitorKey").GetString()!, body);
    }

    public static Task<HttpResponseMessage> SendWebChatAsync(this HttpClient client, string token, string sessionId, string visitorKey, string content) =>
        client.PublicPostAsync("messages", new { token, sessionId, visitorKey, content });
}

public class WebChatTests(AIEnabledFactory factory) : IClassFixture<AIEnabledFactory>
{
    [Fact]
    public async Task Config_ReturnsOnlyPublicSettings()
    {
        var token = await factory.ChannelTokenAsync("Ana");
        var response = await factory.CreateClient().GetAsync($"/api/v1/public/webchat/config?token={token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"agentName\":\"Ana\"", raw);
        Assert.Contains("\"simulatedMode\":false", raw);
        foreach (var secret in new[] { "systemPrompt", "companyId", "allowedOrigins", "encryptedSecrets", "behavior" })
            Assert.DoesNotContain(secret, raw, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("wc_inexistente")]
    [InlineData("")]
    public async Task UnknownToken_Returns404WithoutDetails(string token)
    {
        var response = await factory.CreateClient().GetAsync($"/api/v1/public/webchat/config?token={token}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("channel_unavailable", (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task FullFlow_SessionWelcomeMessageAndAIReply_ArePersisted()
    {
        var client = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Ana");
        var (sessionId, key, body) = await client.StartWebChatAsync(token);
        Assert.Equal("System", body.GetProperty("messages")[0].GetProperty("senderType").GetString());

        factory.AI.Enqueue(_ => new AICompletionResult("O pão francês custa R$ 0,90.", 900, 20, "stop"));
        var response = await client.SendWebChatAsync(token, sessionId, key, "Quanto custa o pão?");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("O pão francês custa R$ 0,90.", raw);
        Assert.DoesNotContain("inputTokens", raw); // visitante não vê dados internos
        Assert.DoesNotContain("sources", raw);

        await using var db = factory.SystemDb();
        var conversation = await db.Conversations.Include(c => c.Messages).FirstAsync(c => c.Id == Guid.Parse(sessionId));
        Assert.Equal(ChannelType.WebChat, conversation.Channel);
        Assert.Equal(3, conversation.Messages.Count);
        Assert.Equal(900, conversation.Messages.Single(m => m.SenderType == SenderType.AI).InputTokens);
        Assert.DoesNotContain(key, conversation.ExternalSessionId); // só o hash é guardado
        Assert.True(await db.AIUsageRecords.AnyAsync(u => u.ConversationId == conversation.Id && u.Channel == ChannelType.WebChat));
    }

    [Fact]
    public async Task Resume_WithCorrectKey_ReturnsSameConversation_WrongKeyStartsNewOne()
    {
        var client = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Ana");
        var (sessionId, key, _) = await client.StartWebChatAsync(token);

        var resumed = await (await client.PublicPostAsync("sessions", new { token, sessionId, visitorKey = key })).ReadJsonAsync();
        Assert.Equal(sessionId, resumed.GetProperty("sessionId").GetString());
        Assert.True(resumed.GetProperty("resumed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, resumed.GetProperty("visitorKey").ValueKind);

        var other = await (await client.PublicPostAsync("sessions", new { token, sessionId, visitorKey = "chave-errada" })).ReadJsonAsync();
        Assert.NotEqual(sessionId, other.GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task WrongVisitorKey_CannotReadOrWriteConversation()
    {
        var client = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Ana");
        var (sessionId, _, _) = await client.StartWebChatAsync(token);

        Assert.Equal(HttpStatusCode.NotFound, (await client.SendWebChatAsync(token, sessionId, "chave-adivinhada", "oi")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PublicPostAsync("messages/list", new { token, sessionId, visitorKey = "chave-adivinhada" })).StatusCode);
    }

    [Fact]
    public async Task ConversationFromAnotherChannel_IsNotReachable()
    {
        var client = factory.CreateClient();
        var (sessionId, key, _) = await client.StartWebChatAsync(await factory.ChannelTokenAsync("Ana"));
        var bellaToken = await factory.ChannelTokenAsync("Bella");

        Assert.Equal(HttpStatusCode.NotFound, (await client.SendWebChatAsync(bellaToken, sessionId, key, "oi")).StatusCode);
    }

    [Fact]
    public async Task AllowedOrigins_AreEnforced()
    {
        var admin = await factory.AuthenticatedAsync("admin@energia.demo");
        var channel = (await (await admin.GetAsync("/api/v1/channels")).ReadJsonAsync())[0];
        var update = await admin.PutJsonAsync($"/api/v1/channels/{channel.GetProperty("id").GetString()}/webchat", new
        {
            agentId = channel.GetProperty("agentId").GetString(),
            name = "Site restrito",
            settings = new { title = "Academia", primaryColor = "#12B76A", position = "right", allowSimulatedReplies = true },
            allowedOrigins = new[] { "https://www.academiaenergia.demo/" }
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var token = channel.GetProperty("publicToken").GetString();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PublicPostAsync("sessions", new { token }, "https://site-pirata.com")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PublicPostAsync("sessions", new { token })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PublicPostAsync("sessions", new { token }, "https://www.academiaenergia.demo")).StatusCode);
    }

    [Fact]
    public async Task ProviderFailure_KeepsCustomerMessage_AndShowsOfflineNotice()
    {
        var client = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Ana");
        var (sessionId, key, _) = await client.StartWebChatAsync(token);

        factory.AI.Enqueue(_ => throw new AIProviderException(AIProviderException.QuotaExceeded, "sem créditos"));
        var result = await (await client.SendWebChatAsync(token, sessionId, key, "Vocês entregam?")).ReadJsonAsync();

        var reply = result.GetProperty("reply");
        Assert.Equal("System", reply.GetProperty("senderType").GetString());
        Assert.Contains("atendente", reply.GetProperty("content").GetString());
        Assert.DoesNotContain("créditos", reply.GetProperty("content").GetString()); // nada interno vaza

        await using var db = factory.SystemDb();
        Assert.True(await db.ConversationMessages.AnyAsync(m => m.ConversationId == Guid.Parse(sessionId) && m.Content == "Vocês entregam?"));
    }

    [Fact]
    public async Task WaitingForHuman_AIDoesNotReply()
    {
        var client = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Ana");
        var (sessionId, key, _) = await client.StartWebChatAsync(token);
        await using (var db = factory.SystemDb())
        {
            (await db.Conversations.FirstAsync(c => c.Id == Guid.Parse(sessionId))).Status = ConversationStatus.WaitingHuman;
            await db.SaveChangesAsync();
        }
        var calls = factory.AI.Requests.Count;

        var result = await (await client.SendWebChatAsync(token, sessionId, key, "Alguém aí?")).ReadJsonAsync();

        Assert.Equal(JsonValueKind.Null, result.GetProperty("reply").ValueKind);
        Assert.Equal(calls, factory.AI.Requests.Count);
    }

    [Fact]
    public async Task ClosedConversation_Returns409()
    {
        var client = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Ana");
        var (sessionId, key, _) = await client.StartWebChatAsync(token);
        await using (var db = factory.SystemDb())
        {
            (await db.Conversations.FirstAsync(c => c.Id == Guid.Parse(sessionId))).Status = ConversationStatus.Closed;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendWebChatAsync(token, sessionId, key, "oi")).StatusCode);
    }

    [Fact]
    public async Task TooLongMessage_IsRejected()
    {
        var client = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Ana");
        var (sessionId, key, _) = await client.StartWebChatAsync(token);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendWebChatAsync(token, sessionId, key, new string('x', 2001))).StatusCode);
    }

    [Fact]
    public async Task PublicEndpoints_AllowCrossOriginWithoutCredentials()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/public/webchat/sessions");
        request.Headers.Add("Origin", "https://qualquer-site.com.br");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task InactiveChannelOrAgent_MakesWidgetUnavailable()
    {
        var admin = await factory.AuthenticatedAsync("admin@petvida.demo");
        var channel = (await (await admin.GetAsync("/api/v1/channels")).ReadJsonAsync())[0];
        var token = channel.GetProperty("publicToken").GetString();
        await admin.PatchJsonAsync($"/api/v1/channels/{channel.GetProperty("id").GetString()}/status", new { status = "Inactive" });

        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync($"/api/v1/public/webchat/config?token={token}")).StatusCode);
    }

    [Fact]
    public async Task RegeneratedToken_InvalidatesOldOne()
    {
        var admin = await factory.AuthenticatedAsync("admin@horizonte.demo");
        var channel = (await (await admin.GetAsync("/api/v1/channels")).ReadJsonAsync())[0];
        var oldToken = channel.GetProperty("publicToken").GetString();

        var updated = await (await admin.PostAsync($"/api/v1/channels/{channel.GetProperty("id").GetString()}/regenerate-token", null)).ReadJsonAsync();
        var newToken = updated.GetProperty("publicToken").GetString();

        Assert.NotEqual(oldToken, newToken);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync($"/api/v1/public/webchat/config?token={oldToken}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync($"/api/v1/public/webchat/config?token={newToken}")).StatusCode);
    }
}

public class WebChatWithoutKeyTests(K2ApiFactory factory) : IClassFixture<K2ApiFactory>
{
    [Fact]
    public async Task WithoutApiKey_RepliesAreSimulatedAndFlagged_OrOfflineWhenDisabled()
    {
        var client = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Ana");
        var config = await (await client.GetAsync($"/api/v1/public/webchat/config?token={token}")).ReadJsonAsync();
        Assert.True(config.GetProperty("simulatedMode").GetBoolean());

        var (sessionId, key, _) = await client.StartWebChatAsync(token);
        var simulated = (await (await client.SendWebChatAsync(token, sessionId, key, "Vocês fazem encomendas de bolo?")).ReadJsonAsync()).GetProperty("reply");
        Assert.True(simulated.GetProperty("isSimulated").GetBoolean());

        // Desligando o modo demonstrativo: mensagem de ausência em vez de resposta simulada.
        var admin = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var channel = (await (await admin.GetAsync("/api/v1/channels")).ReadJsonAsync())[0];
        await admin.PutJsonAsync($"/api/v1/channels/{channel.GetProperty("id").GetString()}/webchat", new
        {
            agentId = channel.GetProperty("agentId").GetString(),
            name = "Site",
            settings = new { title = "Padaria", primaryColor = "#6941C6", position = "right", allowSimulatedReplies = false, offlineMessage = "Voltamos já!" },
            allowedOrigins = Array.Empty<string>()
        });
        var offline = (await (await client.SendWebChatAsync(token, sessionId, key, "Oi")).ReadJsonAsync()).GetProperty("reply");
        Assert.Equal("Voltamos já!", offline.GetProperty("content").GetString());
        Assert.False(offline.GetProperty("isSimulated").GetBoolean());
    }
}
