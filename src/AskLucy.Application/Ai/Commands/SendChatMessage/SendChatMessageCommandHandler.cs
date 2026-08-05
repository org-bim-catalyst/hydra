using AskLucy.Application.Abstractions;
using FluentValidation;
using MediatR;

namespace AskLucy.Application.Ai.Commands.SendChatMessage;

/// <summary>
/// MediatR's IPipelineBehavior validation pipeline covers ordinary requests only —
/// stream requests validate inline here (a dedicated stream pipeline behavior would be
/// over-engineering for the single stream request this migration has, per Simplicity/YAGNI).
/// Resolves the provider by key (specs/005-multi-provider-ai-engine, research.md Decision 3)
/// instead of depending on a single injected <see cref="IAIProvider"/> — this is the seam that
/// makes provider switching a configuration/catalog choice, not a code change.
///
/// <para><b>US1 (specs/016-rag-semantic-search, research.md Decision 8)</b>: retrieves context
/// before building the message list, but only when the conversation has one or more attached
/// knowledge bases — a conversation with none attached is completely unaffected (US1 AC2/AC3).
/// <see cref="IRagService"/> never throws; its result rides the final <see cref="ChatStreamChunk"/>
/// so the controller can attach citations to the persisted assistant message and surface a
/// non-silent retrieval-unavailable warning without blocking the chat response (FR-037a).
/// Chat ownership is not re-validated here — the controller already validated it moments earlier
/// via the user-message <c>AppendMessageCommand</c> call that precedes this one.</para>
/// </summary>
public sealed class SendChatMessageCommandHandler(
    IAIProviderResolver providerResolver,
    IAIProviderRepository providerRepository,
    IAIModelRepository modelRepository,
    IConversationKnowledgeBaseRepository conversationKnowledgeBaseRepository,
    IRagService ragService,
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

        var aiProvider = providerResolver.Resolve(provider.ProviderKey);

        var messages = request.Messages
            .Select(m => new ChatMessage(ParseRole(m.Role), m.Content))
            .ToList();

        var knowledgeBaseIds = (await conversationKnowledgeBaseRepository.GetByConversationAsync(request.ChatId, cancellationToken))
            .Select(l => l.KnowledgeBaseId)
            .ToList();

        RagRetrievalOutcome? retrievalOutcome = null;
        if (knowledgeBaseIds.Count > 0 && request.Messages.Count > 0)
        {
            retrievalOutcome = await ragService.RetrieveContextAsync(
                request.ChatId, request.Messages[^1].Content, knowledgeBaseIds, cancellationToken);

            if (retrievalOutcome.Type == RagRetrievalOutcomeType.Grounded)
            {
                messages.Insert(0, new ChatMessage(ChatRole.System, BuildAugmentedSystemPrompt(retrievalOutcome.ContextText!)));
            }
        }

        await foreach (var chunk in aiProvider.StreamChatAsync(messages, model.ModelKey, request.GenerationParameters, cancellationToken))
        {
            yield return new ChatStreamChunk(chunk.ContentDelta, chunk.Usage);
        }

        if (retrievalOutcome is not null)
        {
            yield return new ChatStreamChunk(null, null, retrievalOutcome);
        }
    }

    private static string BuildAugmentedSystemPrompt(string contextText) =>
        "Use the following retrieved context from the user's knowledge base(s) to answer their " +
        "question. If the context doesn't contain relevant information, say so plainly rather " +
        "than guessing.\n\n<context>\n" + contextText + "\n</context>";

    private static ChatRole ParseRole(string role) => role.ToLowerInvariant() switch
    {
        "system" => ChatRole.System,
        "assistant" => ChatRole.Assistant,
        _ => ChatRole.User,
    };
}
