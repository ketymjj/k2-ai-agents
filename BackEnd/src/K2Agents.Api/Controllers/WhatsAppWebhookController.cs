using K2Agents.Application.WhatsApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace K2Agents.Api.Controllers;

/// <summary>
/// Webhook da WhatsApp Cloud API (configurado no painel de desenvolvedores da Meta). Anônimo por natureza:
/// a autenticidade vem do verify token (GET) e da assinatura HMAC do corpo com o App Secret (POST).
/// </summary>
[ApiController]
[Route("api/v1/webhooks/whatsapp")]
[AllowAnonymous]
public class WhatsAppWebhookController(IWhatsAppWebhookService webhooks) : ControllerBase
{
    private const int MaxBodyBytes = 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        CancellationToken ct)
    {
        var accepted = await webhooks.VerifyAsync(mode, verifyToken, challenge, ct);
        return accepted is null ? StatusCode(StatusCodes.Status403Forbidden) : Content(accepted, "text/plain");
    }

    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        // A assinatura é calculada sobre os bytes exatos do corpo: nada de model binding antes.
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var outcome = await webhooks.ReceiveAsync(buffer.ToArray(), Request.Headers["X-Hub-Signature-256"].ToString(), ct);
        return outcome == WebhookOutcome.Accepted ? Ok() : Unauthorized();
    }
}
