using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Ai.Deprecation;

/// <summary>
/// specs/069 FR-014/FR-017 research D4: moves every affected owner's items to the replacement and
/// sends them one notice. Runs in chunks of owners so a large blast radius never holds one
/// transaction open; a restart resumes by re-reading what still names a retired model.
/// </summary>
public sealed class UserImpactProcessor(
    IModelDeprecationRepository deprecations,
    IAIModelRepository models,
    IModelReferenceStore references,
    INotificationPublisher notifications,
    IUnitOfWork unitOfWork)
{
    public const int OwnerChunkSize = 100;

    public async Task ProcessAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        var batch = await deprecations.GetBatchAsync(batchId, cancellationToken);
        if (batch is null || batch.UserImpactStatus != UserImpactStatus.Pending)
        {
            return;
        }

        var targets = new Dictionary<Guid, RetiredModelTarget>();
        foreach (var deprecation in batch.Deprecations.Where(d => d.ReplacementModelId is not null))
        {
            var replacement = await models.GetByIdAsync(deprecation.ReplacementModelId!.Value, cancellationToken);
            if (replacement is not null)
            {
                targets[deprecation.ModelId] = new RetiredModelTarget(
                    deprecation.Id, replacement.Id, replacement.ProviderId, replacement.ModelKey, replacement.DisplayName);
            }
        }

        var ownerIds = await references.ListOwnerIdsAsync(targets.Keys.ToList(), cancellationToken);
        var notified = 0;
        var now = DateTime.UtcNow;

        foreach (var chunk in ownerIds.Chunk(OwnerChunkSize))
        {
            var switched = await references.SwitchAsync(batchId, targets, chunk, "system", now, cancellationToken);
            foreach (var owner in switched)
            {
                PublishNotice(batchId, owner);
                notified++;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        batch.MarkUserImpactCompleted(notified, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private void PublishNotice(Guid batchId, OwnerSwitchResult owner)
    {
        var modelNames = string.Join(", ", owner.Items.Select(i => i.PreviousModelName).Distinct(StringComparer.Ordinal));
        var byKind = owner.Items.GroupBy(i => i.Kind)
            .Select(g => $"{g.Count()} {Describe(g.Key, g.Count())}");
        var replacements = string.Join(", ", owner.Items.Select(i => i.NewModelName).Distinct(StringComparer.Ordinal));

        notifications.Publish(new NotificationRequest(
            Type: NotificationTypeKeys.SystemAiModelDeprecated,
            Recipient: new NotificationRecipient.User(owner.OwnerUserId),
            Variables: new Dictionary<string, string?>
            {
                ["modelNames"] = modelNames,
                ["itemSummary"] = string.Join(", ", byKind),
                ["replacementSummary"] = replacements,
            },
            RelatedItem: new RelatedItem("ai-model-deprecation-batch", batchId.ToString()),
            EventKey: $"ai-model-deprecation:{batchId}:{owner.OwnerUserId}"));
    }

    private static string Describe(ModelImpactKind kind, int count) => kind switch
    {
        ModelImpactKind.Conversation => count == 1 ? "conversation" : "conversations",
        ModelImpactKind.Agent => count == 1 ? "agent" : "agents",
        ModelImpactKind.Prompt => count == 1 ? "saved prompt" : "saved prompts",
        ModelImpactKind.WorkflowStep => count == 1 ? "workflow step" : "workflow steps",
        _ => "default setting",
    };
}
