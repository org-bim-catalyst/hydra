using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Domain.OperationalFailures;

namespace AskLucy.Application.OperationalFailures;

/// <summary>
/// Maps stored incidents and occurrences to the admin contract shapes, resolving every referenced
/// user and item in one batch per kind rather than per row (specs/074 FR-016).
/// </summary>
public sealed class OperationalFailureReadModelBuilder(IOperationalFailureStore store, IOperationalFailureReferenceLookup references)
{
    public async Task<IReadOnlyList<IncidentSummaryDto>> SummariesAsync(
        IReadOnlyList<OperationalFailureIncident> incidents, CancellationToken cancellationToken)
    {
        if (incidents.Count == 0)
        {
            return [];
        }

        var unresolvedByRootCause = await store.CountUnresolvedByRootCauseAsync(
            incidents.Select(i => i.RootCauseKey).Distinct(StringComparer.Ordinal).ToList(), cancellationToken);

        var subjects = new Dictionary<(ReferencedItemKind, Guid), ItemReference>();
        foreach (var group in incidents
            .Where(i => i.SubjectId is not null && Enum.TryParse<ReferencedItemKind>(i.SubjectType, out _))
            .GroupBy(i => Enum.Parse<ReferencedItemKind>(i.SubjectType!)))
        {
            var found = await references.FindItemsAsync(group.Key, group.Select(i => i.SubjectId!.Value).ToList(), cancellationToken);
            foreach (var (id, item) in found)
            {
                subjects[(group.Key, id)] = item;
            }
        }

        return incidents.Select(i => Summary(i, unresolvedByRootCause, subjects)).ToList();
    }

    public async Task<IReadOnlyList<OccurrenceDto>> OccurrencesAsync(
        IReadOnlyList<OperationalFailureOccurrence> occurrences, CancellationToken cancellationToken)
    {
        if (occurrences.Count == 0)
        {
            return [];
        }

        var users = await UsersAsync(occurrences.Where(o => !o.IsUserErased).Select(o => o.UserId), cancellationToken);
        var chats = await ItemsAsync(ReferencedItemKind.Chat, occurrences.Select(o => o.ChatId), cancellationToken);
        var workflows = await ItemsAsync(ReferencedItemKind.Workflow, occurrences.Select(o => o.WorkflowId), cancellationToken);
        var documents = await ItemsAsync(ReferencedItemKind.Document, occurrences.Select(o => o.DocumentId), cancellationToken);
        var agents = await ItemsAsync(ReferencedItemKind.Agent, occurrences.Select(o => o.AgentId), cancellationToken);
        var mcpServers = await ItemsAsync(ReferencedItemKind.McpServer, occurrences.Select(o => o.McpServerId), cancellationToken);

        return occurrences.Select(o => new OccurrenceDto(
            o.Id,
            o.OccurredAtUtc,
            o.Severity,
            o.Kind,
            o.Reason,
            o.CorrelationId,
            o.IsFailover,
            o.IsUserErased ? UserRefDto.Erased : o.UserId is null ? null : UserRef(o.UserId, users),
            o.ChatId is { } chatId ? new OccurrenceChatDto(chatId, Label(chats, chatId), IsDeleted(chats, chatId)) : null,
            o.MessageId,
            o.WorkflowId is { } workflowId
                ? new OccurrenceWorkflowDto(workflowId, Label(workflows, workflowId), o.WorkflowExecutionId, o.WorkflowExecutionNodeId, IsDeleted(workflows, workflowId))
                : null,
            o.DocumentId is { } documentId
                ? new OccurrenceDocumentDto(documentId, Label(documents, documentId), o.KnowledgeBaseId, IsDeleted(documents, documentId))
                : null,
            o.AgentId is { } agentId
                ? new OccurrenceAgentDto(agentId, Label(agents, agentId), o.AgentExecutionId, IsDeleted(agents, agentId))
                : null,
            o.McpServerId is { } serverId
                ? new OccurrenceMcpServerDto(serverId, Label(mcpServers, serverId), IsDeleted(mcpServers, serverId))
                : null,
            o.JobId,
            o.Engine == OperationalFailureEngine.Access ? o.SourceIp : null)).ToList();
    }

    public async Task<IReadOnlyDictionary<string, UserReference>> UsersAsync(IEnumerable<string?> userIds, CancellationToken cancellationToken)
    {
        var distinct = userIds.OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        return distinct.Count == 0
            ? new Dictionary<string, UserReference>(StringComparer.Ordinal)
            : await references.FindUsersAsync(distinct, cancellationToken);
    }

    /// <summary>A user that no longer resolves has been erased; the trail keeps only the fact of them.</summary>
    public static UserRefDto UserRef(string userId, IReadOnlyDictionary<string, UserReference> users) =>
        users.TryGetValue(userId, out var user)
            ? new UserRefDto(user.Id, user.DisplayName, user.Email, user.IsDeleted ? UserRefStatus.Deleted : UserRefStatus.Active)
            : UserRefDto.Erased;

    private static IncidentSummaryDto Summary(
        OperationalFailureIncident incident,
        IReadOnlyDictionary<string, int> unresolvedByRootCause,
        Dictionary<(ReferencedItemKind, Guid), ItemReference> subjects)
    {
        var sharingRootCause = unresolvedByRootCause.GetValueOrDefault(incident.RootCauseKey);
        var relatedOpen = Math.Max(0, sharingRootCause - (incident.TriageState == IncidentTriageState.Resolved ? 0 : 1));

        IncidentSubjectDto? subject = null;
        if (incident.SubjectType is { } type && incident.SubjectId is { } subjectId)
        {
            // A subject that no longer resolves was purged; one of an unknown type is shown as recorded.
            var known = Enum.TryParse<ReferencedItemKind>(type, out var kind);
            var item = known ? subjects.GetValueOrDefault((kind, subjectId)) : null;
            subject = new IncidentSubjectDto(type, subjectId, item?.Label ?? incident.SubjectLabel, known && (item is null || item.IsDeleted));
        }

        return new IncidentSummaryDto
        {
            Id = incident.Id,
            RowVersion = Convert.ToBase64String(incident.RowVersion),
            Severity = incident.HighestSeverity,
            Engine = incident.Engine,
            Operation = incident.Operation,
            Kind = incident.Kind,
            ProviderId = incident.ProviderId,
            ProviderName = incident.ProviderName,
            Model = incident.Model,
            Subject = subject,
            FirstSeenUtc = incident.FirstSeenUtc,
            LastSeenUtc = incident.LastSeenUtc,
            OccurrenceCount = incident.OccurrenceCount,
            StoredOccurrenceCount = incident.StoredOccurrenceCount,
            DistinctUserCount = incident.DistinctUserCount,
            DistinctSourceCount = incident.DistinctSourceCount,
            RecoveryCount = incident.RecoveryCount,
            LatestReason = incident.LatestReason,
            LatestCorrelationId = incident.LatestCorrelationId,
            State = incident.TriageState,
            RootCauseKey = incident.RootCauseKey,
            RelatedOpenCount = relatedOpen,
            IsRecurrence = incident.IsRecurrence,
        };
    }

    private async Task<IReadOnlyDictionary<Guid, ItemReference>> ItemsAsync(
        ReferencedItemKind kind, IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.OfType<Guid>().Distinct().ToList();
        return distinct.Count == 0
            ? new Dictionary<Guid, ItemReference>()
            : await references.FindItemsAsync(kind, distinct, cancellationToken);
    }

    private static string? Label(IReadOnlyDictionary<Guid, ItemReference> items, Guid id) => items.GetValueOrDefault(id)?.Label;

    /// <summary>An item that no longer resolves was purged, which the trail shows as deleted.</summary>
    private static bool IsDeleted(IReadOnlyDictionary<Guid, ItemReference> items, Guid id) =>
        !items.TryGetValue(id, out var item) || item.IsDeleted;
}
