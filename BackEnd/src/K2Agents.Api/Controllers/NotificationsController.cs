using K2Agents.Api.Infrastructure;
using K2Agents.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace K2Agents.Api.Controllers;

[ApiController]
[Route("api/v1/notifications")]
[Authorize(Policy = Policies.Reader)]
public class NotificationsController(INotificationService notifications) : ControllerBase
{
    [HttpGet]
    public Task<NotificationListDto> List([FromQuery] bool unreadOnly = false, [FromQuery] int take = 20, CancellationToken ct = default) =>
        notifications.ListAsync(unreadOnly, take, ct);

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(ct);
        return NoContent();
    }
}
