using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Flows;
using AskLucy.Application.Conversations.Prompts;
using AskLucy.Application.Options;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Conversations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Conversations.Runtime;

internal static partial class SuggestedActionOfferGeneratorLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Offer for chat {UserChatId} produced {ActionCount} action(s) using {ProviderKey}/{ModelKey} (prompt {PromptVersion})")]
    public static partial void Offered(ILogger logger, Guid userChatId, int actionCount, string providerKey, string modelKey, string promptVersion);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Offer for chat {UserChatId} produced nothing worth suggesting")]
    public static partial void NothingOffered(ILogger logger, Guid userChatId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Offer for chat {UserChatId} dropped a proposed row: {Reason}")]
    public static partial void RowDropped(ILogger logger, Guid userChatId, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Offer step for chat {UserChatId} failed; the turn ends with no offer")]
    public static partial void Failed(ILogger logger, Guid userChatId, Exception exception);
}

/// <summary>Composes and grounds the offer that closes a turn (specs/045 FR-021).</summary>
public interface ISuggestedActionOfferGenerator
{
    Task<SuggestedActionOffer?> GenerateAsync(
        TurnContext context,
        TurnOutcome outcome,
        string justHappened,
        string? memoryContext,
        IReadOnlyList<FlowVariantOfferCandidate>? flowVariantCandidates,
        CancellationToken cancellationToken);
}

/// <summary>
/// <inheritdoc cref="ISuggestedActionOfferGenerator"/>
///
/// <para>
/// One short, non-streaming completion — no corrective retry, unlike <see cref="TurnDecider"/>.
/// The failure matrix treats a failed offer step as ordinary silence ("Log; emit no
/// <c>__ACTIONS__</c>"), not a degradation the user needs to be shielded from with a second model
/// call: the turn already delivered its real content before this step ever runs.
/// </para>
///
/// <para>
/// Model selection reuses <see cref="AiCapability.TurnOrchestration"/> rather than a dedicated
/// capability — its own doc comment already covers "whether to run it or offer it", and both
/// steps are the same short, structured, on-the-critical-path job an administrator would want on
/// the same fast, cheap model (constitution §9).
/// </para>
///
/// <para><b>Never throws.</b> Every failure degrades to no offer at all.</para>
/// </summary>
public sealed class SuggestedActionOfferGenerator(
    AiCapabilityProviderResolver capabilityProviderResolver,
    IAIProviderRepository providerRepository,
    IAIModelRepository modelRepository,
    IAIProviderResolver providerResolver,
    ConversationCapabilityCatalog capabilityCatalog,
    SuggestedActionGrounder grounder,
    IOptions<ConversationRuntimeOptions> options,
    ILogger<SuggestedActionOfferGenerator> logger) : ISuggestedActionOfferGenerator
{
    public async Task<SuggestedActionOffer?> GenerateAsync(
        TurnContext context,
        TurnOutcome outcome,
        string justHappened,
        string? memoryContext,
        IReadOnlyList<FlowVariantOfferCandidate>? flowVariantCandidates,
        CancellationToken cancellationToken)
    {
        try
        {
            var flowVariants = flowVariantCandidates ?? [];
            var offerable = capabilityCatalog.OfferableFor(context, outcome);
            var index = offerable
                .Select(c => new CapabilityIndexEntry(c.Name, c.OfferDescription, c.WhenToUse, c.ArgumentHint))
                .ToList();

            var resolved = await capabilityProviderResolver.ResolveAsync(AiCapability.TurnOrchestration, cancellationToken);
            var provider = await providerRepository.GetByIdAsync(resolved.ProviderId, cancellationToken)
                ?? throw new KeyNotFoundException("The provider assigned to turn orchestration no longer exists.");
            var model = await modelRepository.GetByIdAsync(resolved.ModelId, cancellationToken)
                ?? throw new KeyNotFoundException("The model assigned to turn orchestration no longer exists.");
            var aiProvider = providerResolver.Resolve(provider.ProviderKey);

            var maxSuggestedActions = options.Value.MaxSuggestedActions;
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SuggestedActionPrompt.Build(index, memoryContext, justHappened, Math.Max(1, maxSuggestedActions - 1), flowVariants)),
                new(ChatRole.User, "Compose the offer now."),
            };

            var parameters = new GenerationParametersDto(JsonMode: model.SupportsJsonMode ? true : null);
            var completion = await aiProvider.ChatAsync(messages, model.ModelKey, parameters, cancellationToken);

            var result = grounder.Ground(completion.Content, context, capabilityCatalog, maxSuggestedActions, flowVariants);

            foreach (var reason in result.DroppedReasons)
            {
                SuggestedActionOfferGeneratorLog.RowDropped(logger, context.UserChatId, reason);
            }

            if (result.Offer is null)
            {
                SuggestedActionOfferGeneratorLog.NothingOffered(logger, context.UserChatId);
                return null;
            }

            SuggestedActionOfferGeneratorLog.Offered(
                logger, context.UserChatId, result.Offer.Actions.Count, provider.ProviderKey, model.ModelKey, SuggestedActionPrompt.Version);

            return result.Offer;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            SuggestedActionOfferGeneratorLog.Failed(logger, context.UserChatId, ex);
            return null;
        }
    }
}
