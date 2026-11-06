using K2Agents.Api.Infrastructure;
using K2Agents.Application.Common;
using K2Agents.Application.Conversations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace K2Agents.Api.Controllers;

[ApiController]
[Route("api/v1/conversations")]
[Authorize(Policy = Policies.Reader)]
public class ConversationsController(IConversationService conversations) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ConversationSummaryDto>> List([FromQuery] ConversationQuery query, CancellationToken ct) => conversations.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<ConversationDetailDto> Get(Guid id, CancellationToken ct) => conversations.GetAsync(id, ct);

    [HttpPost("{id:guid}/close")]
    [Authorize(Policy = Policies.Attendant)]
    public Task<ConversationSummaryDto> Close(Guid id, CancellationToken ct) => conversations.CloseAsync(id, ct);

    [HttpPost("{id:guid}/reopen")]
    [Authorize(Policy = Policies.Attendant)]
    public Task<ConversationSummaryDto> Reopen(Guid id, CancellationToken ct) => conversations.ReopenAsync(id, ct);

    [HttpGet("{id:guid}/events")]
    public Task<IReadOnlyList<ConversationEventDto>> Events(Guid id, CancellationToken ct) => conversations.EventsAsync(id, ct);

    /// <summary>Transferência manual para a fila humana (a IA para de responder).</summary>
    [HttpPost("{id:guid}/transfer")]
    [Authorize(Policy = Policies.Attendant)]
    public Task<ConversationSummaryDto> Transfer(Guid id, CancellationToken ct) => conversations.TransferAsync(id, ct);

    [HttpPost("{id:guid}/claim")]
    [Authorize(Policy = Policies.Attendant)]
    public Task<ConversationSummaryDto> Claim(Guid id, CancellationToken ct) => conversations.ClaimAsync(id, ct);

    [HttpPost("{id:guid}/messages")]
    [Authorize(Policy = Policies.Attendant)]
    public Task<MessageDto> Reply(Guid id, ReplyRequest request, CancellationToken ct) => conversations.ReplyAsync(id, request, ct);

    [HttpPost("{id:guid}/return-to-ai")]
    [Authorize(Policy = Policies.Attendant)]
    public Task<ConversationSummaryDto> ReturnToAi(Guid id, CancellationToken ct) => conversations.ReturnToAiAsync(id, ct);

    [HttpPost("{id:guid}/contact")]
    [Authorize(Policy = Policies.Attendant)]
    public Task<ConversationDetailDto> LinkContact(Guid id, LinkContactRequest request, CancellationToken ct) =>
        conversations.LinkContactAsync(id, request, ct);
}

/// <summary>Mensagens de uma conversa (atalho REST para integrações).</summary>
[ApiController]
[Route("api/v1/messages")]
[Authorize(Policy = Policies.Reader)]
public class MessagesController(IConversationService conversations) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<MessageDto>> List([FromQuery] Guid conversationId, CancellationToken ct) =>
        (await conversations.GetAsync(conversationId, ct)).Messages;
}
