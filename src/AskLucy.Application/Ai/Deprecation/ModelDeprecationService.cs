using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.ApplyProviderModelSync;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Common;
using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Ai.Deprecation;

/// <summary>
/// specs/069 research D4/D5: deprecates each candidate, picks its replacement, and moves or flags
/// the platform defaults it filled. Runs inside the caller's unit of work; the caller saves once.
/// A candidate that cannot be deprecated is reported and leaves nothing half-changed.
/// </summary>
public sealed class ModelDeprecationService(
    IAIModelRepository models,
    IAIProviderRepository providers,
    IAiCapabilityAssignmentRepository assignments,
    IModelDeprecationRepository deprecations,
    INotificationPublisher notifications,
    AdminRecipientResolver adminRecipients) : IModelDeprecationService
{
    public async Task<ModelDeprecationResult> DeprecateAsync(
        Guid providerId,
        IReadOnlyList<DeprecationCandidate> candidates,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var deprecatedKeys = new List<string>();
        var failed = new List<SyncApplyFailureDto>();
        if (candidates.Count == 0)
        {
            return new ModelDeprecationResult(null, deprecatedKeys, failed);
        }

        var provider = await providers.GetByIdAsync(providerId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider not found.");
        var catalog = await models.ListByProviderIdAsync(providerId, cancellationToken);
        var providerAssignments = (await assignments.ListAllAsync(cancellationToken))
            .Where(a => a.ProviderId == providerId)
            .ToList();

        var now = DateTime.UtcNow;
        var batchModelIds = candidates.Select(c => c.Model.Id).ToHashSet();
        var batch = ModelDeprecationBatch.Deprecate(providerId, actor, now);

        foreach (var candidate in candidates)
        {
            var retired = candidate.Model;
            try
            {
                var replacement = ResolveReplacement(candidate, catalog, batchModelIds);
                retired.Deprecate(actor);

                var source = replacement.Model is null ? ReplacementSource.None
                    : candidate.Choice.Kind == ReplacementChoiceKind.Model ? ReplacementSource.AdminChosen
                    : ReplacementSource.Proposed;
                var deprecation = ModelDeprecation.Create(batch.Id, retired.Id, replacement.Model?.Id, source, actor, now);

                ReassignOrFlagProviderDefault(provider, deprecation, retired, replacement.Model, actor, now);
                ReassignOrFlagCapabilityAssignments(providerAssignments, deprecation, retired, replacement.Model, providerId, actor, now);

                batch.Add(deprecation);
                deprecatedKeys.Add(retired.ModelKey);
            }
            catch (DomainRuleViolationException ex)
            {
                failed.Add(new SyncApplyFailureDto(retired.ModelKey, retired.DisplayName, ex.Message));
            }
        }

        if (deprecatedKeys.Count > 0)
        {
            deprecations.Add(batch);
            await PublishSummariesAsync(batch, provider.DisplayName, actor, now, cancellationToken);
        }

        return new ModelDeprecationResult(deprecatedKeys.Count > 0 ? batch.Id : null, deprecatedKeys, failed);
    }

    /// <summary>specs/069 FR-018/FR-020b: one summary per admin, chunked to the hub's recipient limit.</summary>
    private async Task PublishSummariesAsync(
        ModelDeprecationBatch batch, string providerName, string actor, DateTime now, CancellationToken cancellationToken)
    {
        var recipients = await adminRecipients.ResolveAsync(cancellationToken);
        if (recipients.Mandatory.Count == 0 && recipients.Optional.Count == 0)
        {
            return;
        }

        var catalog = await models.ListByProviderIdAsync(batch.ProviderId, cancellationToken);
        var names = catalog.Where(m => batch.Deprecations.Any(d => d.ModelId == m.Id))
            .Select(m => m.DisplayName).ToList();
        var reassigned = batch.Deprecations.SelectMany(d => d.DefaultChanges).Count(c => c.Outcome == DefaultChangeOutcome.Reassigned);
        var flagged = batch.Deprecations.SelectMany(d => d.DefaultChanges).Count(c => c.Outcome == DefaultChangeOutcome.Flagged);

        var variables = new Dictionary<string, string?>
        {
            ["providerName"] = providerName,
            ["modelNames"] = string.Join(", ", names),
            ["reason"] = "No longer listed by the vendor",
            ["confirmedBy"] = actor,
            ["confirmedAt"] = $"{now:yyyy-MM-dd HH:mm} UTC",
            ["reassignedSummary"] = reassigned == 0 ? "No platform defaults were reassigned." : $"{reassigned} platform default(s) were reassigned.",
            ["flaggedSummary"] = flagged == 0 ? "No defaults need attention." : $"{flagged} platform default(s) need attention on the AI Providers page.",
            ["validationRequest"] = "Please review and validate the replacement.",
        };

        Publish(NotificationTypeKeys.SystemAiModelDeprecationSummary, recipients.Mandatory);
        Publish(NotificationTypeKeys.SystemAiModelDeprecationSummarySuperUser, recipients.Optional);

        void Publish(string type, IReadOnlyList<string> userIds)
        {
            foreach (var chunk in userIds.Chunk(100))
            {
                notifications.Publish(new NotificationRequest(
                    Type: type,
                    Recipient: new NotificationRecipient.Users(chunk),
                    Variables: variables,
                    RelatedItem: new RelatedItem("ai-model-deprecation-batch", batch.Id.ToString()),
                    EventKey: $"ai-model-deprecation-summary:{batch.Id}:{chunk[0]}"));
            }
        }
    }

    private static ResolvedReplacement ResolveReplacement(DeprecationCandidate candidate, IReadOnlyList<AIModel> catalog, HashSet<Guid> batchModelIds)
    {
        var retired = candidate.Model;
        switch (candidate.Choice.Kind)
        {
            case ReplacementChoiceKind.None:
                return new ResolvedReplacement(null);

            case ReplacementChoiceKind.Model:
                var chosen = catalog.FirstOrDefault(m => m.Id == candidate.Choice.ModelId)
                    ?? throw new DomainRuleViolationException("The chosen replacement is not in this provider's catalog.");
                if (batchModelIds.Contains(chosen.Id))
                {
                    throw new DomainRuleViolationException("The chosen replacement is being deprecated in the same confirmation.");
                }

                if (!chosen.IsSelectable)
                {
                    throw new DomainRuleViolationException("The chosen replacement is not Available.");
                }

                var missing = ReplacementModelSelector.MissingCapabilities(retired, chosen);
                if (missing.Count > 0)
                {
                    throw new DomainRuleViolationException($"The chosen replacement lacks: {string.Join(", ", missing)}.");
                }

                return new ResolvedReplacement(chosen);

            default:
                var ranked = ReplacementModelSelector.Rank(retired, catalog, batchModelIds);
                return new ResolvedReplacement(ranked.Count > 0 ? ranked[0] : null);
        }
    }

    private static void ReassignOrFlagProviderDefault(
        AIProvider provider, ModelDeprecation deprecation, AIModel retired, AIModel? replacement, string actor, DateTime now)
    {
        if (provider.DefaultModelId != retired.Id)
        {
            return;
        }

        if (replacement is not null && PlatformFunctionRequirements.SatisfiesProviderDefault(replacement))
        {
            provider.SetDefaultModel(replacement.Id, actor);
            deprecation.AddDefaultChange(PlatformDefaultChange.Reassign(
                DefaultTargetKind.ProviderDefault, provider.Id, null, retired.Id, replacement.Id, actor, now));
            return;
        }

        provider.SetDefaultModel(null, actor);
        deprecation.AddDefaultChange(PlatformDefaultChange.Flag(
            DefaultTargetKind.ProviderDefault, provider.Id, null, retired.Id,
            replacement is null ? DefaultFlagReason.NoEligibleReplacement : DefaultFlagReason.MissingRequiredCapability,
            servingProviderId: null, servingModelId: null, actor, now));
    }

    private void ReassignOrFlagCapabilityAssignments(
        IReadOnlyList<AiCapabilityAssignment> providerAssignments,
        ModelDeprecation deprecation,
        AIModel retired,
        AIModel? replacement,
        Guid providerId,
        string actor,
        DateTime now)
    {
        foreach (var assignment in providerAssignments.Where(a => a.ModelId == retired.Id).ToList())
        {
            if (replacement is not null && PlatformFunctionRequirements.Satisfies(assignment.Capability, replacement))
            {
                assignment.AssignTo(providerId, replacement.Id, actor);
                deprecation.AddDefaultChange(PlatformDefaultChange.Reassign(
                    DefaultTargetKind.CapabilityAssignment, providerId, assignment.Capability, retired.Id, replacement.Id, actor, now));
                continue;
            }

            // An image-generation assignment must name a model (it never falls back to the
            // provider default), so flagging it removes the assignment and leaves it unconfigured.
            if (assignment.Capability == AiCapability.ImageGeneration)
            {
                assignments.Remove(assignment);
            }
            else
            {
                assignment.AssignTo(providerId, null, actor);
            }

            deprecation.AddDefaultChange(PlatformDefaultChange.Flag(
                DefaultTargetKind.CapabilityAssignment, providerId, assignment.Capability, retired.Id,
                replacement is null ? DefaultFlagReason.NoEligibleReplacement : DefaultFlagReason.MissingRequiredCapability,
                servingProviderId: null, servingModelId: null, actor, now));
        }
    }

    private sealed record ResolvedReplacement(AIModel? Model);
}
