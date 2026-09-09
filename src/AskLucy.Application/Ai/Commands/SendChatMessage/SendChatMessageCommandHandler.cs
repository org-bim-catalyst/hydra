using AskLucy.Application.Abstractions;
using AskLucy.Application.Conversations.Runtime;
using FluentValidation;
using MediatR;

namespace AskLucy.Application.Ai.Commands.SendChatMessage;

/// <summary>
/// The MediatR seam for a chat turn: validate the request, resolve which provider and model will
/// serve it, and hand the turn itself to <see cref="IConversationTurnOrchestrator"/>.
///
/// <para>
/// MediatR's <c>IPipelineBehavior</c> validation pipeline covers ordinary requests only — stream
/// requests validate inline here (a dedicated stream pipeline behavior would be over-engineering
/// for the single stream request this migration has, per Simplicity/YAGNI). Resolving the provider
/// by key (specs/005-multi-provider-ai-engine, research.md Decision 3) instead of depending on a
/// single injected <see cref="IAIProvider"/> is the seam that makes provider switching a
/// configuration/catalog choice, not a code change.
/// </para>
///
/// <para>
/// <b>Why this is now three statements long.</b> Until specs/045 T007–T010 this method was 356
/// lines carrying six concerns — retrieval, memory, location, zoom, boundary and transport — with
/// no name for the thing it was actually doing. The turn logic moved to
/// <see cref="ConversationTurnOrchestrator"/> verbatim, guarded by
/// <c>SendChatMessageCommandHandlerCharacterisationTests</c>, which pins the emitted chunk
/// sequence and passed unchanged either side of the move. Doing this <i>first</i> was the point:
/// the agentic runtime replaces that pipeline rather than stacking a second orchestrator on top
/// of it.
/// </para>
///
/// <para>
/// Chat ownership is not re-validated here — the controller already validated it moments earlier
/// via the user-message <c>AppendMessageCommand</c> call that precedes this one.
/// </para>
/// </summary>
public sealed class SendChatMessageCommandHandler(
    IAIProviderResolver providerResolver,
    IAIProviderRepository providerRepository,
    IAIModelRepository modelRepository,
    IConversationTurnOrchestrator turnOrchestrator,
    IValidator<SendChatMessageCommand> validator) : IStreamRequestHandler<SendChatMessageCommand, ChatStreamChunk>
{
    public async IAsyncEnumerable<ChatStreamChunk> Handle(
        SendChatMessageCommand request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        // The validator already confirmed these exist/are enabled/available — re-fetching
        // here (rather than threading the entities through) keeps the validator's job
        // purely "is this request valid" and the handler's job purely "execute it".
        var provider = await providerRepository.GetByIdAsync(request.ProviderId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider not found.");
        var model = await modelRepository.GetByIdAsync(request.ModelId, cancellationToken)
            ?? throw new KeyNotFoundException("Model not found.");

        var turn = new ConversationTurnRequest(
            request.ChatId,
            request.Messages,
            providerResolver.Resolve(provider.ProviderKey),
            model.ModelKey,
            request.GenerationParameters,
            request.SelectedAction);

        await foreach (var chunk in turnOrchestrator.RunAsync(turn, cancellationToken))
        {
            yield return chunk;
        }
    }
}
