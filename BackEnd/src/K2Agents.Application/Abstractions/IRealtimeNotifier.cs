using K2Agents.Application.Conversations;
using K2Agents.Application.Notifications;

namespace K2Agents.Application.Abstractions;

/// <summary>
/// Publica eventos em tempo real (SignalR). A central de atendimento recebe eventos da própria empresa;
/// o visitante do Web Chat recebe apenas as mensagens públicas da própria conversa.
/// </summary>
public interface IRealtimeNotifier
{
    Task MessageCreatedAsync(Guid companyId, Guid conversationId, MessageDto message, CancellationToken ct);
    Task ConversationUpdatedAsync(Guid companyId, ConversationSummaryDto conversation, CancellationToken ct);
    /// <summary>Notificação geral vai para a empresa; com UserId, apenas para aquele usuário.</summary>
    Task NotificationCreatedAsync(Guid companyId, NotificationDto notification, CancellationToken ct);
}
