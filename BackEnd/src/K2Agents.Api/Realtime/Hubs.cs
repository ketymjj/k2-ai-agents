using K2Agents.Application.Abstractions;
using K2Agents.Application.Common;
using K2Agents.Application.Conversations;
using K2Agents.Application.Notifications;
using K2Agents.Application.WebChat;
using K2Agents.Domain.Enums;
using K2Agents.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace K2Agents.Api.Realtime;

public static class RealtimeGroups
{
    public const string Platform = "platform";
    public static string Company(Guid companyId) => $"company:{companyId:N}";
    public static string Visitor(Guid conversationId) => $"visitor:{conversationId:N}";
}

/// <summary>
/// Central de atendimento: cada usuário entra no grupo da própria empresa (Super Admin: plataforma).
/// Usa Context.User: em alguns transportes o hub roda fora da requisição HTTP e o IHttpContextAccessor fica vazio.
/// </summary>
[Authorize]
public class InboxHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        var role = user?.FindFirst(K2ClaimTypes.Role)?.Value;
        if (role == nameof(UserRole.SuperAdmin))
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Platform);
        else if (Guid.TryParse(user?.FindFirst(K2ClaimTypes.CompanyId)?.Value, out var companyId))
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Company(companyId));
        else
            Context.Abort();
        await base.OnConnectedAsync();
    }
}

/// <summary>Visitante do Web Chat: só entra no grupo da conversa após validar token do canal + chave da sessão.</summary>
[AllowAnonymous]
public class WebChatHub(IWebChatService webChat) : Hub
{
    public async Task<bool> Join(string token, Guid sessionId, string visitorKey)
    {
        try
        {
            var origin = Context.GetHttpContext()?.Request.Headers.Origin.ToString();
            var conversationId = await webChat.AuthorizeVisitorAsync(token, sessionId, visitorKey, origin, Context.ConnectionAborted);
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Visitor(conversationId));
            return true;
        }
        catch (AppException)
        {
            return false;
        }
    }
}

/// <summary>Identifica o usuário do SignalR pela claim "sub" do JWT (permite Clients.User(id)).</summary>
public class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User?.FindFirst(K2ClaimTypes.Subject)?.Value;
}

public class SignalRNotifier(IHubContext<InboxHub> inbox, IHubContext<WebChatHub> visitors, ILogger<SignalRNotifier> logger) : IRealtimeNotifier
{
    public async Task MessageCreatedAsync(Guid companyId, Guid conversationId, MessageDto message, CancellationToken ct)
    {
        await SafeAsync(async () =>
        {
            var payload = new { conversationId, message };
            await inbox.Clients.Groups(RealtimeGroups.Company(companyId), RealtimeGroups.Platform).SendAsync("messageCreated", payload, ct);
            // O visitante recebe só a versão pública (sem tokens, custos ou fontes internas).
            var publicMessage = new PublicMessageDto(message.Id, message.SenderType, message.Content, message.CreatedAt, message.IsSimulated);
            await visitors.Clients.Group(RealtimeGroups.Visitor(conversationId)).SendAsync("message", publicMessage, ct);
        });
    }

    public Task ConversationUpdatedAsync(Guid companyId, ConversationSummaryDto conversation, CancellationToken ct) =>
        SafeAsync(() => inbox.Clients.Groups(RealtimeGroups.Company(companyId), RealtimeGroups.Platform)
            .SendAsync("conversationUpdated", conversation, ct));

    public Task NotificationCreatedAsync(Guid companyId, NotificationDto notification, CancellationToken ct) =>
        SafeAsync(() => notification.UserId is { } userId
            ? inbox.Clients.User(userId.ToString()).SendAsync("notification", notification, ct)
            : inbox.Clients.Group(RealtimeGroups.Company(companyId)).SendAsync("notification", notification, ct));

    /// <summary>Falha de entrega em tempo real nunca derruba a operação que já foi gravada.</summary>
    private async Task SafeAsync(Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Falha ao publicar evento em tempo real.");
        }
    }
}
