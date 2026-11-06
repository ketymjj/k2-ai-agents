using System.Security.Cryptography;
using System.Text.Json;
using FluentValidation;
using K2Agents.Application.Abstractions;
using K2Agents.Application.Common;
using K2Agents.Application.WhatsApp;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace K2Agents.Application.Channels;

public interface IChannelService
{
    Task<IReadOnlyList<ChannelDto>> ListAsync(Guid? companyId, CancellationToken ct);
    Task<ChannelDto> GetAsync(Guid id, CancellationToken ct);
    Task<ChannelDto> CreateWebChatAsync(SaveWebChatRequest request, CancellationToken ct);
    Task<ChannelDto> UpdateWebChatAsync(Guid id, SaveWebChatRequest request, CancellationToken ct);
    Task<ChannelDto> SetStatusAsync(Guid id, ChannelStatus status, CancellationToken ct);
    Task<ChannelDto> RegenerateTokenAsync(Guid id, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<ChannelDto> CreateWhatsAppAsync(SaveWhatsAppRequest request, CancellationToken ct);
    Task<ChannelDto> UpdateWhatsAppAsync(Guid id, SaveWhatsAppRequest request, CancellationToken ct);
    Task<WhatsAppConnectionResult> TestWhatsAppAsync(Guid id, CancellationToken ct);
}

/// <summary>Canais de atendimento. ChannelConfiguration é ITenantEntity (filtro global por empresa).</summary>
public class ChannelService(
    IAppDbContext db, ICurrentUser currentUser, IAuditService audit, IValidator<SaveWebChatRequest> validator,
    IValidator<SaveWhatsAppRequest> whatsAppValidator, ISecretProtector secrets, IWhatsAppClient whatsApp) : IChannelService
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static WebChatSettings ParseSettings(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<WebChatSettings>(json, Json) ?? new WebChatSettings();
        }
        catch (JsonException)
        {
            return new WebChatSettings();
        }
    }

    /// <summary>Token público aleatório (192 bits). Identifica o canal; não concede acesso a dados.</summary>
    public static string NewPublicToken() => "wc_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public async Task<IReadOnlyList<ChannelDto>> ListAsync(Guid? companyId, CancellationToken ct)
    {
        var channels = db.ChannelConfigurations.AsNoTracking();
        if (companyId.HasValue) channels = channels.Where(c => c.CompanyId == companyId);
        var rows = await Project(channels.OrderBy(c => c.Name)).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<ChannelDto> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await Project(db.ChannelConfigurations.AsNoTracking().Where(c => c.Id == id)).FirstOrDefaultAsync(ct)
                  ?? throw AppException.NotFound("Canal");
        return ToDto(row);
    }

    public async Task<ChannelDto> CreateWebChatAsync(SaveWebChatRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var agent = await ResolveAgentAsync(request.CompanyId, request.AgentId, ct);

        var channel = new ChannelConfiguration
        {
            CompanyId = agent.CompanyId,
            AgentId = agent.Id,
            ChannelType = ChannelType.WebChat,
            Status = ChannelStatus.Active,
            PublicToken = NewPublicToken()
        };
        Apply(channel, request);
        db.ChannelConfigurations.Add(channel);
        audit.Record("channel.created", nameof(ChannelConfiguration), channel.Id.ToString(), $"Web Chat: {channel.Name}", channel.CompanyId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(channel.Id, ct);
    }

    public async Task<ChannelDto> UpdateWebChatAsync(Guid id, SaveWebChatRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var channel = await LoadAsync(id, ct);
        var agent = await ResolveAgentAsync(channel.CompanyId, request.AgentId, ct);
        if (agent.CompanyId != channel.CompanyId) throw AppException.Forbidden();

        channel.AgentId = agent.Id;
        Apply(channel, request);
        channel.UpdatedAt = DateTime.UtcNow;
        audit.Record("channel.updated", nameof(ChannelConfiguration), id.ToString(), channel.Name, channel.CompanyId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ChannelDto> SetStatusAsync(Guid id, ChannelStatus status, CancellationToken ct)
    {
        if (status is not (ChannelStatus.Active or ChannelStatus.Inactive)) throw new AppException("Status inválido.", 400, "invalid_status");
        var channel = await LoadAsync(id, ct);
        if (status == ChannelStatus.Active && channel.ChannelType == ChannelType.WhatsApp && !ReadSecrets(channel).IsComplete)
            throw new AppException("Configure o access token e o App Secret antes de ativar o canal do WhatsApp.", 409, "credentials_required");
        if (channel.Status != status)
        {
            channel.Status = status;
            channel.UpdatedAt = DateTime.UtcNow;
            audit.Record(status == ChannelStatus.Active ? "channel.activated" : "channel.deactivated",
                nameof(ChannelConfiguration), id.ToString(), channel.Name, channel.CompanyId);
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    /// <summary>Invalida o token atual (ex.: widget copiado para um site não autorizado).</summary>
    public async Task<ChannelDto> RegenerateTokenAsync(Guid id, CancellationToken ct)
    {
        var channel = await LoadAsync(id, ct);
        channel.PublicToken = NewPublicToken();
        channel.UpdatedAt = DateTime.UtcNow;
        audit.Record("channel.token_regenerated", nameof(ChannelConfiguration), id.ToString(), channel.Name, channel.CompanyId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var channel = await LoadAsync(id, ct);
        db.ChannelConfigurations.Remove(channel);
        audit.Record("channel.deleted", nameof(ChannelConfiguration), id.ToString(), channel.Name, channel.CompanyId);
        await db.SaveChangesAsync(ct);
    }

    // ---------- WhatsApp Business Platform (Cloud API) ----------

    public async Task<ChannelDto> CreateWhatsAppAsync(SaveWhatsAppRequest request, CancellationToken ct)
    {
        await whatsAppValidator.ValidateAndThrowAsync(request, ct);
        var agent = await ResolveAgentAsync(request.CompanyId, request.AgentId, ct);
        await EnsurePhoneNumberFreeAsync(request.PhoneNumberId.Trim(), null, ct);

        var channel = new ChannelConfiguration
        {
            CompanyId = agent.CompanyId,
            AgentId = agent.Id,
            ChannelType = ChannelType.WhatsApp,
            SettingsJson = new WhatsAppSettings { VerifyToken = NewVerifyToken() }.Serialize()
        };
        ApplyWhatsApp(channel, request);
        // Sem credenciais completas, o canal fica aguardando; ativar é um passo explícito, depois do teste de conexão.
        channel.Status = ReadSecrets(channel).IsComplete ? ChannelStatus.Inactive : ChannelStatus.PendingCredentials;
        db.ChannelConfigurations.Add(channel);
        audit.Record("channel.created", nameof(ChannelConfiguration), channel.Id.ToString(), $"WhatsApp: {channel.Name}", channel.CompanyId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(channel.Id, ct);
    }

    public async Task<ChannelDto> UpdateWhatsAppAsync(Guid id, SaveWhatsAppRequest request, CancellationToken ct)
    {
        await whatsAppValidator.ValidateAndThrowAsync(request, ct);
        var channel = await LoadAsync(id, ct);
        if (channel.ChannelType != ChannelType.WhatsApp) throw AppException.NotFound("Canal");
        var agent = await ResolveAgentAsync(channel.CompanyId, request.AgentId, ct);
        if (agent.CompanyId != channel.CompanyId) throw AppException.Forbidden();
        await EnsurePhoneNumberFreeAsync(request.PhoneNumberId.Trim(), channel.Id, ct);

        channel.AgentId = agent.Id;
        ApplyWhatsApp(channel, request);
        var complete = ReadSecrets(channel).IsComplete;
        if (!complete) channel.Status = ChannelStatus.PendingCredentials;
        else if (channel.Status == ChannelStatus.PendingCredentials) channel.Status = ChannelStatus.Inactive;
        channel.UpdatedAt = DateTime.UtcNow;
        // A auditoria registra QUE as credenciais mudaram, nunca o valor.
        var changed = new[] { request.AccessToken is { Length: > 0 } ? "access token" : null, request.AppSecret is { Length: > 0 } ? "app secret" : null }
            .Where(s => s is not null).ToList();
        audit.Record("channel.updated", nameof(ChannelConfiguration), id.ToString(),
            changed.Count > 0 ? $"{channel.Name} (credenciais atualizadas: {string.Join(", ", changed)})" : channel.Name, channel.CompanyId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Consulta o número na Graph API com o token salvo. Chamada externa real: só funciona com credenciais válidas.</summary>
    public async Task<WhatsAppConnectionResult> TestWhatsAppAsync(Guid id, CancellationToken ct)
    {
        var channel = await LoadAsync(id, ct);
        if (channel.ChannelType != ChannelType.WhatsApp) throw AppException.NotFound("Canal");
        var settings = WhatsAppSettings.Parse(channel.SettingsJson);
        var token = ReadSecrets(channel).AccessToken;
        if (string.IsNullOrWhiteSpace(token)) return new WhatsAppConnectionResult(false, null, null, "missing_access_token");

        try
        {
            var info = await whatsApp.GetPhoneNumberAsync(settings.PhoneNumberId, token, ct);
            if (string.IsNullOrWhiteSpace(settings.DisplayPhoneNumber) && !string.IsNullOrWhiteSpace(info.DisplayPhoneNumber))
            {
                channel.SettingsJson = (settings with { DisplayPhoneNumber = info.DisplayPhoneNumber }).Serialize();
                channel.UpdatedAt = DateTime.UtcNow;
            }
            audit.Record("channel.whatsapp_tested", nameof(ChannelConfiguration), id.ToString(), "ok", channel.CompanyId);
            await db.SaveChangesAsync(ct);
            return new WhatsAppConnectionResult(true, info.DisplayPhoneNumber, info.VerifiedName, null);
        }
        catch (WhatsAppApiException ex)
        {
            audit.Record("channel.whatsapp_tested", nameof(ChannelConfiguration), id.ToString(), ex.Code, channel.CompanyId);
            await db.SaveChangesAsync(ct);
            return new WhatsAppConnectionResult(false, null, null, ex.Code);
        }
    }

    /// <summary>Credenciais decifradas. Falha de decifragem (chave trocada) equivale a "sem credenciais".</summary>
    private WhatsAppSecrets ReadSecrets(ChannelConfiguration channel) => WhatsAppCredentials.Read(secrets, channel);

    private void ApplyWhatsApp(ChannelConfiguration channel, SaveWhatsAppRequest r)
    {
        channel.Name = r.Name.Trim();
        var current = WhatsAppSettings.Parse(channel.SettingsJson);
        channel.SettingsJson = (current with
        {
            PhoneNumberId = r.PhoneNumberId.Trim(),
            BusinessAccountId = ValidationRules.TrimToNull(r.BusinessAccountId),
            DisplayPhoneNumber = ValidationRules.TrimToNull(r.DisplayPhoneNumber) ?? current.DisplayPhoneNumber
        }).Serialize();
        channel.ExternalAccountId = r.PhoneNumberId.Trim();

        var saved = ReadSecrets(channel);
        var updated = new WhatsAppSecrets(
            string.IsNullOrWhiteSpace(r.AccessToken) ? saved.AccessToken : r.AccessToken.Trim(),
            string.IsNullOrWhiteSpace(r.AppSecret) ? saved.AppSecret : r.AppSecret.Trim());
        channel.EncryptedSecrets = updated.AccessToken is null && updated.AppSecret is null ? null : WhatsAppCredentials.Protect(secrets, updated);
    }

    private async Task EnsurePhoneNumberFreeAsync(string phoneNumberId, Guid? exceptId, CancellationToken ct)
    {
        var taken = await db.ChannelConfigurations.IgnoreQueryFilters().AnyAsync(c =>
            c.ChannelType == ChannelType.WhatsApp && c.ExternalAccountId == phoneNumberId && c.Id != exceptId, ct);
        if (taken) throw new AppException("Este número de WhatsApp já está conectado a outro canal.", 409, "phone_number_in_use");
    }

    private static string NewVerifyToken() =>
        "k2v_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private async Task<ChannelConfiguration> LoadAsync(Guid id, CancellationToken ct) =>
        await db.ChannelConfigurations.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw AppException.NotFound("Canal");

    private async Task<AIAgent> ResolveAgentAsync(Guid? companyId, Guid agentId, CancellationToken ct)
    {
        if (!currentUser.IsSuperAdmin && companyId.HasValue && companyId != currentUser.CompanyId) throw AppException.Forbidden();
        var agent = await db.AIAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, ct) ?? throw AppException.NotFound("Agente");
        if (companyId.HasValue && agent.CompanyId != companyId)
            throw new AppException("O agente pertence a outra empresa.", 400, "agent_company_mismatch");
        return agent;
    }

    private static void Apply(ChannelConfiguration channel, SaveWebChatRequest r)
    {
        channel.Name = r.Name.Trim();
        channel.SettingsJson = JsonSerializer.Serialize(r.Settings with
        {
            Title = r.Settings.Title.Trim(),
            PrimaryColor = r.Settings.PrimaryColor.ToUpperInvariant(),
            WelcomeMessage = ValidationRules.TrimToNull(r.Settings.WelcomeMessage),
            OfflineMessage = ValidationRules.TrimToNull(r.Settings.OfflineMessage),
            Subtitle = ValidationRules.TrimToNull(r.Settings.Subtitle)
        }, Json);
        var origins = (r.AllowedOrigins ?? []).Select(ChannelOrigins.Normalize).Where(o => o is not null).Distinct().ToList();
        channel.AllowedOrigins = origins.Count > 0 ? string.Join(',', origins) : null;
    }

    private sealed record Row(ChannelConfiguration Channel, string? CompanyName, string AgentName, int Conversations);

    private IQueryable<Row> Project(IQueryable<ChannelConfiguration> query) =>
        query.Select(c => new Row(
            c,
            db.Companies.Where(co => co.Id == c.CompanyId).Select(co => co.Name).FirstOrDefault(),
            c.Agent!.Name,
            db.Conversations.Count(cv => cv.AgentId == c.AgentId && cv.Channel == c.ChannelType)));

    private ChannelDto ToDto(Row r) => new(
        r.Channel.Id, r.Channel.CompanyId, r.CompanyName, r.Channel.AgentId, r.AgentName, r.Channel.ChannelType, r.Channel.Name,
        r.Channel.Status, r.Channel.PublicToken, ParseSettings(r.Channel.SettingsJson), ChannelOrigins.Parse(r.Channel.AllowedOrigins),
        r.Conversations, r.Channel.CreatedAt, r.Channel.UpdatedAt,
        r.Channel.ChannelType == ChannelType.WhatsApp ? WhatsAppInfo(r.Channel) : null);

    private WhatsAppChannelInfo WhatsAppInfo(ChannelConfiguration channel)
    {
        var s = WhatsAppSettings.Parse(channel.SettingsJson);
        var saved = ReadSecrets(channel);
        return new WhatsAppChannelInfo(s.PhoneNumberId, s.BusinessAccountId, s.DisplayPhoneNumber, s.VerifyToken,
            !string.IsNullOrWhiteSpace(saved.AccessToken), !string.IsNullOrWhiteSpace(saved.AppSecret));
    }
}
