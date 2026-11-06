using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using K2Agents.Tests.Support;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Tests.Api;

public class InboxTests(AIEnabledFactory factory) : IClassFixture<AIEnabledFactory>
{
    [Fact]
    public async Task List_ShowsDemoConversationsOfOwnCompany_AndHidesPlayground()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        await client.StartSessionAsync(await factory.AgentIdAsync("Ana")); // sessão de Playground

        var list = await (await client.GetAsync("/api/v1/conversations?pageSize=100")).ReadJsonAsync();
        var items = list.GetProperty("items").EnumerateArray().ToList();

        Assert.NotEmpty(items);
        Assert.All(items, c => Assert.Equal("Padaria Jardim Andrade", c.GetProperty("companyName").GetString()));
        Assert.DoesNotContain(items, c => c.GetProperty("channel").GetString() == "Playground");
        Assert.Contains(items, c => c.GetProperty("isDemo").GetBoolean());
    }

    [Fact]
    public async Task FilterAndSearch_Work()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);

        var waiting = await (await client.GetAsync("/api/v1/conversations?status=WaitingHuman")).ReadJsonAsync();
        Assert.All(waiting.GetProperty("items").EnumerateArray(), c => Assert.Equal("WaitingHuman", c.GetProperty("status").GetString()));

        var search = await (await client.GetAsync("/api/v1/conversations?search=cobertura derretida")).ReadJsonAsync();
        Assert.Equal(1, search.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Detail_ReturnsMessages_AndIsTenantIsolated()
    {
        var padaria = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var id = (await (await padaria.GetAsync("/api/v1/conversations")).ReadJsonAsync()).GetProperty("items")[0].GetProperty("id").GetString();

        var detail = await (await padaria.GetAsync($"/api/v1/conversations/{id}")).ReadJsonAsync();
        Assert.True(detail.GetProperty("messages").GetArrayLength() > 0);

        var bella = await factory.AuthenticatedAsync(Demo.BellaAdmin);
        Assert.Equal(HttpStatusCode.NotFound, (await bella.GetAsync($"/api/v1/conversations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bella.PostAsync($"/api/v1/conversations/{id}/close", null)).StatusCode);
    }

    [Fact]
    public async Task CloseAndReopen_ChangeStatus_AddSystemMessages_AndRespectRoles()
    {
        var token = await factory.ChannelTokenAsync("Ana");
        var (sessionId, _, _) = await factory.CreateClient().StartWebChatAsync(token);

        var viewer = await factory.AuthenticatedAsync(Demo.PadariaViewer);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/v1/conversations/{sessionId}/close", null)).StatusCode);

        var attendant = await factory.AuthenticatedAsync(Demo.PadariaAttendant);
        var closed = await (await attendant.PostAsync($"/api/v1/conversations/{sessionId}/close", null)).ReadJsonAsync();
        Assert.Equal("Closed", closed.GetProperty("status").GetString());

        var reopened = await (await attendant.PostAsync($"/api/v1/conversations/{sessionId}/reopen", null)).ReadJsonAsync();
        Assert.Equal("Open", reopened.GetProperty("status").GetString());

        await using var db = factory.SystemDb();
        var system = await db.ConversationMessages.Where(m => m.ConversationId == Guid.Parse(sessionId) && m.SenderType == Domain.Enums.SenderType.System)
            .Select(m => m.Content).ToListAsync();
        Assert.Contains("Conversa encerrada.", system);
        Assert.Contains("Conversa reaberta.", system);
    }
}

public class ChannelsTests(K2ApiFactory factory) : IClassFixture<K2ApiFactory>
{
    private static object WebChat(string agentId, string color = "#6941C6", string[]? origins = null) => new
    {
        agentId,
        name = "Novo canal",
        settings = new { title = "Fale conosco", primaryColor = color, position = "left", allowSimulatedReplies = true },
        allowedOrigins = origins ?? []
    };

    [Fact]
    public async Task Create_GeneratesPublicToken_AndNormalizesOrigins()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var response = await client.PostJsonAsync("/api/v1/channels/webchat",
            WebChat(await factory.AgentIdAsync("Ana"), "#12b76a", ["https://WWW.Padaria.demo/cardapio"]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var channel = await response.ReadJsonAsync();
        Assert.StartsWith("wc_", channel.GetProperty("publicToken").GetString());
        Assert.Equal("#12B76A", channel.GetProperty("settings").GetProperty("primaryColor").GetString());
        Assert.Equal("https://www.padaria.demo", channel.GetProperty("allowedOrigins")[0].GetString());
    }

    [Fact]
    public async Task Create_RejectsInvalidSettings()
    {
        var client = await factory.AuthenticatedAsync(Demo.PadariaAdmin);
        var response = await client.PostJsonAsync("/api/v1/channels/webchat",
            WebChat(await factory.AgentIdAsync("Ana"), "vermelho", ["javascript:alert(1)"]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Tenancy_And_Roles()
    {
        var bella = await factory.AuthenticatedAsync(Demo.BellaAdmin);
        Assert.Equal(HttpStatusCode.NotFound,
            (await bella.PostJsonAsync("/api/v1/channels/webchat", WebChat(await factory.AgentIdAsync("Ana")))).StatusCode);
        var bellaChannels = await (await bella.GetAsync("/api/v1/channels")).ReadJsonAsync();
        Assert.All(bellaChannels.EnumerateArray(), c => Assert.Equal("Studio Bella", c.GetProperty("companyName").GetString()));

        var viewer = await factory.AuthenticatedAsync(Demo.PadariaViewer);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PostJsonAsync("/api/v1/channels/webchat", WebChat(await factory.AgentIdAsync("Ana")))).StatusCode);
    }
}

/// <summary>SignalR de ponta a ponta contra o servidor de teste.</summary>
public class RealtimeTests(AIEnabledFactory factory) : IClassFixture<AIEnabledFactory>
{
    private async Task<HubConnection> InboxAsync(string email)
    {
        var login = await (await factory.CreateClient().LoginAsync(email, Demo.Password)).ReadJsonAsync();
        var token = login.GetProperty("accessToken").GetString();
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/inbox"), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult(token);
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }

    [Fact]
    public async Task WebChatMessage_IsPushedToOwnCompanyInbox_ButNotToOthers()
    {
        var padaria = await InboxAsync(Demo.PadariaAttendant);
        var bella = await InboxAsync(Demo.BellaAdmin);
        var padariaEvents = new ConcurrentBag<JsonElement>();
        var bellaEvents = new ConcurrentBag<JsonElement>();
        var received = new TaskCompletionSource();
        padaria.On<JsonElement>("messageCreated", e =>
        {
            padariaEvents.Add(e);
            if (e.GetProperty("message").GetProperty("content").GetString() == "Mensagem em tempo real") received.TrySetResult();
        });
        bella.On<JsonElement>("messageCreated", e => bellaEvents.Add(e));

        var client = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Ana");
        var (sessionId, key, _) = await client.StartWebChatAsync(token);
        await client.SendWebChatAsync(token, sessionId, key, "Mensagem em tempo real");

        await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(500);
        Assert.Contains(padariaEvents, e => e.GetProperty("conversationId").GetString() == sessionId);
        Assert.DoesNotContain(bellaEvents, e => e.GetProperty("conversationId").GetString() == sessionId);

        await padaria.DisposeAsync();
        await bella.DisposeAsync();
    }

    [Fact]
    public async Task InboxHub_RequiresAuthentication()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/inbox"), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
    }

    [Fact]
    public async Task VisitorHub_JoinRequiresValidKey_AndDeliversOnlyPublicFields()
    {
        var client = factory.CreateClient();
        var token = await factory.ChannelTokenAsync("Ana");
        var (sessionId, key, _) = await client.StartWebChatAsync(token);

        var visitor = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/webchat"), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        var messages = new ConcurrentBag<string>();
        var gotReply = new TaskCompletionSource();
        visitor.On<JsonElement>("message", m =>
        {
            messages.Add(m.GetRawText());
            if (m.GetProperty("senderType").GetString() == "AI") gotReply.TrySetResult();
        });
        await visitor.StartAsync();

        Assert.False(await visitor.InvokeAsync<bool>("Join", token, Guid.Parse(sessionId), "chave-errada"));
        Assert.True(await visitor.InvokeAsync<bool>("Join", token, Guid.Parse(sessionId), key));

        await client.SendWebChatAsync(token, sessionId, key, "Oi");
        await gotReply.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.All(messages, m => Assert.DoesNotContain("inputTokens", m, StringComparison.OrdinalIgnoreCase));
        await visitor.DisposeAsync();
    }
}
