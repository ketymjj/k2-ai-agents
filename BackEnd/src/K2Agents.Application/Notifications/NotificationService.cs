using K2Agents.Application.Abstractions;
using K2Agents.Application.Common;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Application.Notifications;

public record NotificationDto(
    Guid Id, Guid CompanyId, Guid? UserId, NotificationType Type, string Title, string Message, string? Link, DateTime CreatedAt, bool IsRead);

public record NotificationListDto(IReadOnlyList<NotificationDto> Items, int Unread);

public interface INotificationService
{
    /// <summary>Grava e publica em tempo real. UserId nulo = todos os usuários da empresa.</summary>
    Task<NotificationDto> CreateAsync(Guid companyId, NotificationType type, string title, string message, string? link, Guid? userId, CancellationToken ct);
    Task<NotificationListDto> ListAsync(bool unreadOnly, int take, CancellationToken ct);
    Task MarkReadAsync(Guid id, CancellationToken ct);
    Task MarkAllReadAsync(CancellationToken ct);
}

/// <summary>
/// Notificações internas. "Lida" é por notificação (simples e suficiente para equipes pequenas):
/// quem lê uma notificação geral a marca para a empresa toda.
/// </summary>
public class NotificationService(IAppDbContext db, ICurrentUser currentUser, IRealtimeNotifier realtime) : INotificationService
{
    public async Task<NotificationDto> CreateAsync(
        Guid companyId, NotificationType type, string title, string message, string? link, Guid? userId, CancellationToken ct)
    {
        var notification = new Notification
        {
            CompanyId = companyId,
            UserId = userId,
            Type = type,
            Title = Truncate(title, 160),
            Message = Truncate(message, 1000),
            Link = link
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);
        var dto = ToDto(notification);
        await realtime.NotificationCreatedAsync(companyId, dto, ct);
        return dto;
    }

    public async Task<NotificationListDto> ListAsync(bool unreadOnly, int take, CancellationToken ct)
    {
        var mine = Visible();
        var unread = await mine.CountAsync(n => n.ReadAt == null, ct);
        var query = unreadOnly ? mine.Where(n => n.ReadAt == null) : mine;
        var items = await query.OrderByDescending(n => n.CreatedAt).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct);
        return new NotificationListDto(items.Select(ToDto).ToList(), unread);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        var notification = await Visible().FirstOrDefaultAsync(n => n.Id == id, ct) ?? throw AppException.NotFound("Notificação");
        if (notification.ReadAt is not null) return;
        notification.ReadAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(CancellationToken ct)
    {
        var unread = await Visible().Where(n => n.ReadAt == null).ToListAsync(ct);
        foreach (var n in unread) n.ReadAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Notificações da empresa (filtro global) destinadas a todos ou ao próprio usuário.</summary>
    private IQueryable<Notification> Visible()
    {
        var userId = currentUser.UserId;
        return db.Notifications.Where(n => n.UserId == null || n.UserId == userId);
    }

    public static NotificationDto ToDto(Notification n) =>
        new(n.Id, n.CompanyId, n.UserId, n.Type, n.Title, n.Message, n.Link, n.CreatedAt, n.ReadAt is not null);

    private static string Truncate(string text, int max) => text.Length > max ? text[..max] : text;
}
