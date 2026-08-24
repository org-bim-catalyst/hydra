using AskLucy.Application.Abstractions;
using AskLucy.Application.Locations;
using FluentValidation;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Options;

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
///
/// <para><b>AI Memory System (specs/018-ai-memory-system, research.md Decisions 2/3/9)</b>:
/// retrieves relevant memories and, when found, inserts their own <c>ChatRole.System</c> message
/// via a second <c>Insert(0, ...)</c> call made *after* RAG's — placing the memory context ahead
/// of RAG's in the final message list (research.md Decision 2). <see cref="IMemoryService"/>
/// never throws (constitution §2.VIII); its outcome rides the final <see cref="ChatStreamChunk"/>
/// exactly like <see cref="IRagService"/>'s, so a memory-subsystem outage degrades the response
/// gracefully rather than blocking it (spec.md FR-014a).</para>
/// </summary>
public sealed class SendChatMessageCommandHandler(
    IAIProviderResolver providerResolver,
    IAIProviderRepository providerRepository,
    IAIModelRepository modelRepository,
    IConversationKnowledgeBaseRepository conversationKnowledgeBaseRepository,
    IRagService ragService,
    IMemoryService memoryService,
    ILocationResolutionService locationResolutionService,
    IUserChatRepository userChatRepository,
    ICurrentUserAccessor currentUser,
    IBackgroundJobClient backgroundJobClient,
    IOptions<LocationResolutionOptions> locationResolutionOptions,
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
                messages.Insert(0, new ChatMessage(ChatRole.System, RetrievalPromptFraming.BuildRagSystemMessage(retrievalOutcome.ContextText!)));
            }
        }

        MemoryRetrievalOutcome? memoryOutcome = null;
        var userId = currentUser.UserId;
        var chat = await userChatRepository.GetByIdAsync(request.ChatId, cancellationToken);
        if (userId is not null && request.Messages.Count > 0)
        {
            memoryOutcome = await memoryService.RetrieveRelevantMemoriesAsync(
                userId, request.ChatId, chat?.ProjectId, request.Messages[^1].Content, cancellationToken);

            if (memoryOutcome.Type == MemoryRetrievalOutcomeType.Found)
            {
                messages.Insert(0, new ChatMessage(ChatRole.System, RetrievalPromptFraming.BuildMemorySystemMessage(memoryOutcome.ContextText!)));
            }
        }

        // specs/037-location-query-resolution FR-008: launch location resolution concurrently
        // with the model's text stream — never blocking first byte.
        var turnStartUtc = DateTime.UtcNow;
        var activeLocation = chat?.ActiveLocation;
        var latestUserMessage = request.Messages.Count > 0 ? request.Messages[^1].Content : string.Empty;
        var locationTask = locationResolutionService.ResolveAsync(
            userId, request.ChatId, latestUserMessage, activeLocation, cancellationToken);

        await foreach (var chunk in aiProvider.StreamChatAsync(messages, model.ModelKey, request.GenerationParameters, cancellationToken))
        {
            yield return new ChatStreamChunk(chunk.ContentDelta, chunk.Usage);
        }

        // Await the location task with the remaining budget from ResolutionCeilingSeconds (FR-013).
        var ceiling = locationResolutionOptions.Value.ResolutionCeilingSeconds;
        var elapsed = DateTime.UtcNow - turnStartUtc;
        var remaining = TimeSpan.FromSeconds(ceiling) - elapsed;
        LocationResolutionOutcome locationOutcome;
        if (remaining <= TimeSpan.Zero && !locationTask.IsCompletedSuccessfully)
        {
            // Budget already elapsed and task hasn't finished — treat as Unavailable immediately.
            locationOutcome = new LocationResolutionOutcome(LocationResolutionOutcomeType.Unavailable, null,
                LocationConfirmationTemplates.Unavailable);
        }
        else
        {
            try
            {
                locationOutcome = remaining > TimeSpan.Zero
                    ? await locationTask.WaitAsync(remaining, CancellationToken.None)
                    : await locationTask; // Task already completed successfully — retrieve result.
            }
            catch (TimeoutException)
            {
                locationOutcome = new LocationResolutionOutcome(LocationResolutionOutcomeType.Unavailable, null,
                    LocationConfirmationTemplates.Unavailable);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Client disconnected — re-throw so the iterator terminates cleanly (I2).
                throw;
            }
            catch (OperationCanceledException)
            {
                // Internal task cancellation (not client disconnect) → Unavailable.
                locationOutcome = new LocationResolutionOutcome(LocationResolutionOutcomeType.Unavailable, null,
                    LocationConfirmationTemplates.Unavailable);
            }
        }

        // Append the deterministic confirmation/explanation sentence if the intent was non-NoIntent.
        if (locationOutcome.Type != LocationResolutionOutcomeType.NoIntent && locationOutcome.ConfirmationText is not null)
        {
            yield return new ChatStreamChunk(locationOutcome.ConfirmationText, null);
        }

        if (retrievalOutcome is not null || memoryOutcome is not null || locationOutcome.ConfirmedLocation is not null)
        {
            yield return new ChatStreamChunk(null, null, retrievalOutcome, memoryOutcome, locationOutcome.ConfirmedLocation);
        }

        // spec.md FR-006 (research.md Decision 6) — fire-and-forget background analysis of this
        // turn for new candidate memories. Enqueued against the interface, never the concrete
        // type, so Hangfire resolves it through the container (same idiom DocumentProcessingPipeline
        // already uses); never awaited/blocking, and its own failures never surface here — retried
        // by its own [AutomaticRetry] attribute, with MemoryExtractionSweepJob as the safety net if
        // even the enqueue itself fails.
        backgroundJobClient.Enqueue<IMemoryExtractionJob>(j => j.RunAsync(request.ChatId, CancellationToken.None));
    }

    private static ChatRole ParseRole(string role) => role.ToLowerInvariant() switch
    {
        "system" => ChatRole.System,
        "assistant" => ChatRole.Assistant,
        _ => ChatRole.User,
    };
}
