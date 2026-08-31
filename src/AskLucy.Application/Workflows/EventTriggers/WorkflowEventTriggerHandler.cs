using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Workflows;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Workflows.EventTriggers;

/// <summary>
/// Matches a published Documents/KnowledgeBases event against every published, enabled Event-Driven
/// <see cref="Workflow"/> (research.md Decision 12) and starts a matching execution — the
/// event-trigger equivalent of <c>StartWorkflowExecutionCommandHandler</c>, run as the workflow's own
/// owner (the user who configured the trigger) rather than whichever user happened to cause the
/// triggering event (spec.md Acceptance Scenario 9.2: "the <em>initiating user's</em> permissions").
/// One class implementing <see cref="INotificationHandler{TNotification}"/> once per event type,
/// per tasks.md T192's "one per event type" (three overloads, one file), all funneling into the
/// same <see cref="DispatchAsync"/>.
/// </summary>
public sealed class WorkflowEventTriggerHandler(
    IWorkflowRepository workflowRepository,
    IWorkflowExecutionRepository executionRepository,
    IWorkflowPolicyRepository policyRepository,
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IWorkflowExecutionRunner runner,
    IWorkflowAuditLogRepository auditLogRepository,
    IOptions<WorkflowRuntimeOptions> workflowRuntimeOptions,
    IUnitOfWork unitOfWork,
    ILogger<WorkflowEventTriggerHandler> logger)
    : INotificationHandler<DocumentUploadedNotification>,
      INotificationHandler<DocumentProcessedNotification>,
      INotificationHandler<KnowledgeBaseUpdatedNotification>
{
    private const string SystemActor = "system:event-trigger";
    private static readonly JsonSerializerOptions InputJsonOptions = new(JsonSerializerDefaults.Web);

    public Task Handle(DocumentUploadedNotification notification, CancellationToken cancellationToken) =>
        DispatchAsync(
            "DocumentUploaded",
            notification.KnowledgeBaseId,
            requiredOwnerId: null, // Re-checked against the knowledge base's own current owner below instead.
            new { eventType = "DocumentUploaded", notification.DocumentId, notification.KnowledgeBaseId },
            new { documentId = notification.DocumentId, knowledgeBaseId = notification.KnowledgeBaseId, fileName = notification.FileName },
            cancellationToken);

    public Task Handle(DocumentProcessedNotification notification, CancellationToken cancellationToken) =>
        DispatchAsync(
            "DocumentProcessed",
            scopeKnowledgeBaseId: null,
            // FR-064/Acceptance Scenario 9.2 — DocumentProcessed has no knowledge-base scope to
            // re-check ownership against, but it still MUST be scoped to the triggering document's
            // own owner: without this, any user could configure an unscoped DocumentProcessed
            // trigger (the only configuration the UI offers for this event type) and have every
            // other user's processed document id/filename pushed into their own execution history.
            // Documents have no ownership-transfer concept in this codebase, so comparing directly
            // against the notification's own OwnerId (rather than a repository re-fetch, as the
            // knowledge-base-scoped events below do) is not weaker — there is no mutable ownership
            // state that could have gone stale between publish and dispatch.
            requiredOwnerId: notification.OwnerId,
            new { eventType = "DocumentProcessed", notification.DocumentId },
            new { documentId = notification.DocumentId, fileName = notification.FileName },
            cancellationToken);

    public Task Handle(KnowledgeBaseUpdatedNotification notification, CancellationToken cancellationToken) =>
        DispatchAsync(
            "KnowledgeBaseUpdated",
            notification.KnowledgeBaseId,
            requiredOwnerId: null, // Re-checked against the knowledge base's own current owner below instead.
            new { eventType = "KnowledgeBaseUpdated", notification.KnowledgeBaseId },
            new { knowledgeBaseId = notification.KnowledgeBaseId },
            cancellationToken);

    /// <summary>
    /// <paramref name="scopeKnowledgeBaseId"/> is the event's own knowledge base, used both for
    /// scope matching (FR-064) and for the owner-authorization re-check (Acceptance Scenario 9.2) —
    /// null for event types with no knowledge-base scope (<c>DocumentProcessed</c>), which then only
    /// match a trigger configured with no scope at all. <paramref name="requiredOwnerId"/> is a
    /// second, independent authorization check for event types with no knowledge-base scope to
    /// re-check against (currently only <c>DocumentProcessed</c>) — every candidate workflow whose
    /// owner doesn't match is skipped, exactly like the knowledge-base re-check below.
    /// </summary>
    private async Task DispatchAsync(
        string eventType, Guid? scopeKnowledgeBaseId, string? requiredOwnerId, object triggeringEventReference, object inputPayload,
        CancellationToken cancellationToken)
    {
        var candidates = await workflowRepository.ListPublishedEventDrivenAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return;
        }

        var triggeringEventReferenceJson = JsonSerializer.Serialize(triggeringEventReference, InputJsonOptions);
        var inputsJson = JsonSerializer.Serialize(inputPayload, InputJsonOptions);

        foreach (var (workflow, version) in candidates)
        {
            if (workflow.Status != WorkflowStatus.Published)
            {
                // Acceptance Scenario 9.3 — disabling/archiving/deprecating stops dispatch.
                // Defense-in-depth alongside IWorkflowRepository.ListPublishedEventDrivenAsync's
                // own Published-only filter, since this handler never re-fetches the workflow.
                continue;
            }

            var config = WorkflowEventTriggerConfigurationParser.Parse(workflow.EventTriggerConfigurationJson);
            if (!string.Equals(config.EventType, eventType, StringComparison.Ordinal))
            {
                continue;
            }

            if (config.KnowledgeBaseId is { } configuredKnowledgeBaseId && configuredKnowledgeBaseId != scopeKnowledgeBaseId)
            {
                continue; // FR-064 — scope doesn't match this trigger.
            }

            if (scopeKnowledgeBaseId is { } knowledgeBaseId)
            {
                // FR-064/Acceptance Scenario 9.2 — re-check the WORKFLOW OWNER's current access
                // (not the event's own actor's), since the trigger runs as whoever configured it.
                var knowledgeBase = await knowledgeBaseRepository.GetByIdAsync(knowledgeBaseId, cancellationToken);
                if (knowledgeBase is null || knowledgeBase.OwnerId != workflow.OwnerId)
                {
                    WorkflowEventTriggerHandlerLog.KnowledgeBaseAccessLost(logger, workflow.Id, workflow.OwnerId, knowledgeBaseId);
                    auditLogRepository.Add(WorkflowAuditLog.Create(
                        workflow.Id, null, workflow.OwnerId, WorkflowAuditAction.PermissionDenied,
                        JsonSerializer.Serialize(new { eventType, knowledgeBaseId }, InputJsonOptions)));
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    continue;
                }
            }

            if (requiredOwnerId is not null && requiredOwnerId != workflow.OwnerId)
            {
                // FR-064/Acceptance Scenario 9.2 — an event type with no knowledge-base scope
                // (DocumentProcessed) is still scoped to the triggering resource's own owner; a
                // workflow belonging to anyone else never sees it.
                WorkflowEventTriggerHandlerLog.OwnerDoesNotOwnResource(logger, workflow.Id, workflow.OwnerId);
                auditLogRepository.Add(WorkflowAuditLog.Create(
                    workflow.Id, null, workflow.OwnerId, WorkflowAuditAction.PermissionDenied,
                    JsonSerializer.Serialize(new { eventType }, InputJsonOptions)));
                await unitOfWork.SaveChangesAsync(cancellationToken);
                continue;
            }

            var userLimit = await policyRepository.GetUserExecutionLimitAsync(workflow.OwnerId, cancellationToken);
            var maxConcurrentExecutions = userLimit?.MaxConcurrentExecutions ?? workflowRuntimeOptions.Value.DefaultMaxConcurrentExecutions;
            var activeCount = await executionRepository.CountActiveByUserAsync(workflow.OwnerId, cancellationToken);
            if (activeCount >= maxConcurrentExecutions)
            {
                // FR-070/Acceptance Scenario 9.4 — a burst of matching events never exceeds the
                // owner's concurrency cap; excess events are simply skipped, not queued indefinitely.
                WorkflowEventTriggerHandlerLog.ConcurrencyCapReached(logger, workflow.Id, workflow.OwnerId, maxConcurrentExecutions);
                continue;
            }

            var execution = WorkflowExecution.Create(
                workflow.Id, version.Id, workflow.OwnerId, WorkflowExecutionTriggerType.EventDriven,
                triggeringEventReferenceJson, inputsJson, SystemActor);

            executionRepository.Add(execution);
            auditLogRepository.Add(WorkflowAuditLog.Create(workflow.Id, execution.Id, workflow.OwnerId, WorkflowAuditAction.ExecutionStarted, "{}"));
            await unitOfWork.SaveChangesAsync(cancellationToken);

            await runner.EnqueueAsync(execution.Id, cancellationToken);
        }
    }
}

/// <summary>CA1848 — LoggerMessage delegates for <see cref="WorkflowEventTriggerHandler"/>'s dispatch-skip paths.</summary>
internal static partial class WorkflowEventTriggerHandlerLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Event-driven workflow {WorkflowId} was not started: owner {OwnerId} no longer has access to knowledge base {KnowledgeBaseId}.")]
    public static partial void KnowledgeBaseAccessLost(ILogger logger, Guid workflowId, string ownerId, Guid knowledgeBaseId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Event-driven workflow {WorkflowId} was not started: owner {OwnerId} does not own the resource that triggered this event.")]
    public static partial void OwnerDoesNotOwnResource(ILogger logger, Guid workflowId, string ownerId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Event-driven workflow {WorkflowId} was not started: owner {OwnerId} is at their concurrency cap ({MaxConcurrentExecutions}).")]
    public static partial void ConcurrencyCapReached(ILogger logger, Guid workflowId, string ownerId, int maxConcurrentExecutions);
}
