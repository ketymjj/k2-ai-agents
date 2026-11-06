using System.Text.Json;
using K2Agents.Domain.Entities;
using K2Agents.Domain.Enums;

namespace K2Agents.Application.Conversations;

public record MessageDto(
    Guid Id, SenderType SenderType, string Content, DateTime CreatedAt, int InputTokens, int OutputTokens, int? ResponseTimeMs, bool IsSimulated,
    IReadOnlyList<KnowledgeSourceRef> Sources);

/// <summary>Mensagem como o visitante do Web Chat a vê: sem tokens, custos ou fontes internas.</summary>
public record PublicMessageDto(Guid Id, SenderType SenderType, string Content, DateTime CreatedAt, bool IsSimulated);

public static class MessageMapping
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static MessageDto ToDto(ConversationMessage m) =>
        new(m.Id, m.SenderType, m.Content, m.CreatedAt, m.InputTokens, m.OutputTokens, m.ResponseTimeMs, m.IsSimulated, ParseSources(m.KnowledgeSources));

    public static PublicMessageDto ToPublic(ConversationMessage m) => new(m.Id, m.SenderType, m.Content, m.CreatedAt, m.IsSimulated);

    public static string? SerializeSources(IReadOnlyList<KnowledgeSourceRef> sources) =>
        sources.Count > 0 ? JsonSerializer.Serialize(sources, Json) : null;

    private static IReadOnlyList<KnowledgeSourceRef> ParseSources(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<KnowledgeSourceRef>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
