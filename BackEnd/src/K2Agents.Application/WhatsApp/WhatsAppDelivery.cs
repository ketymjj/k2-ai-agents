using K2Agents.Application.Abstractions;
using K2Agents.Application.Common;
using K2Agents.Application.Conversations;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Application.WhatsApp;

/// <summary>
/// Entrega as respostas dos atendentes em conversas do WhatsApp. Regra da Meta: mensagens livres só dentro de
/// 24 h desde a última mensagem do cliente; fora disso, apenas modelos aprovados (não suportados aqui).
/// </summary>
public class WhatsAppDelivery(IAppDbContext db, ISecretProtector secrets, IWhatsAppClient whatsApp) : IChannelDelivery
{
    public static readonly TimeSpan CustomerServiceWindow = TimeSpan.FromHours(24);

    public async Task<string?> DeliverHumanReplyAsync(Conversation conversation, string text, CancellationToken ct)
    {
        if (conversation.Channel != ChannelType.WhatsApp) return null;

        var lastCustomer = await db.ConversationMessages.IgnoreQueryFilters()
            .Where(m => m.ConversationId == conversation.Id && m.SenderType == SenderType.Contact)
            .MaxAsync(m => (DateTime?)m.CreatedAt, ct);
        if (lastCustomer is null || DateTime.UtcNow - lastCustomer.Value > CustomerServiceWindow)
            throw new AppException("A janela de 24 horas do WhatsApp expirou: aguarde uma nova mensagem do cliente para responder.", 409, "whatsapp_window_closed");

        var channel = await db.ChannelConfigurations.IgnoreQueryFilters()
            .Where(c => c.CompanyId == conversation.CompanyId && c.ChannelType == ChannelType.WhatsApp)
            .OrderByDescending(c => c.AgentId == conversation.AgentId).FirstOrDefaultAsync(ct);
        var token = channel is null ? null : WhatsAppCredentials.Read(secrets, channel).AccessToken;
        if (channel is null || string.IsNullOrWhiteSpace(token) || conversation.ExternalSessionId?.StartsWith(WhatsAppMessageProcessor.SessionPrefix) != true)
            throw new AppException("Não foi possível enviar a mensagem pelo WhatsApp. Verifique as credenciais do canal.", 502, "whatsapp_send_failed");

        try
        {
            var to = conversation.ExternalSessionId[WhatsAppMessageProcessor.SessionPrefix.Length..];
            return await whatsApp.SendTextAsync(WhatsAppSettings.Parse(channel.SettingsJson).PhoneNumberId, token, to, text, ct);
        }
        catch (WhatsAppApiException)
        {
            throw new AppException("Não foi possível enviar a mensagem pelo WhatsApp. Verifique as credenciais do canal.", 502, "whatsapp_send_failed");
        }
    }
}
