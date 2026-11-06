using K2Agents.Api.Infrastructure;
using K2Agents.Application.Conversations;
using K2Agents.Application.WebChat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace K2Agents.Api.Controllers;

/// <summary>
/// API pública usada pelo widget em sites de terceiros: anônima, CORS aberto sem credenciais,
/// limite por IP e validação da origem contra os domínios autorizados do canal.
/// </summary>
[ApiController]
[Route("api/v1/public/webchat")]
[AllowAnonymous]
[EnableCors(CorsPolicies.PublicWidget)]
[EnableRateLimiting(RateLimitPolicies.WebChat)]
public class PublicWebChatController(IWebChatService webChat) : ControllerBase
{
    private string? Origin => Request.Headers.Origin.FirstOrDefault();

    [HttpGet("config")]
    public Task<WebChatConfigDto> Config([FromQuery] string? token, CancellationToken ct) => webChat.GetConfigAsync(token ?? string.Empty, Origin, ct);

    [HttpPost("sessions")]
    public Task<WebChatSessionDto> Start(StartWebChatRequest request, CancellationToken ct) => webChat.StartSessionAsync(request, Origin, ct);

    [HttpPost("messages")]
    public Task<WebChatSendResult> Send(SendWebChatMessageRequest request, CancellationToken ct) => webChat.SendAsync(request, Origin, ct);

    /// <summary>POST para que a chave do visitante não apareça em URLs e logs de acesso.</summary>
    [HttpPost("messages/list")]
    public Task<IReadOnlyList<PublicMessageDto>> Messages(ListWebChatMessagesRequest request, CancellationToken ct) =>
        webChat.MessagesAsync(request.Token, request.SessionId, request.VisitorKey, request.After, Origin, ct);
}

public record ListWebChatMessagesRequest(string Token, Guid SessionId, string VisitorKey, DateTime? After);

public static class CorsPolicies
{
    public const string PublicWidget = "public-widget";
}
