using System.Diagnostics.CodeAnalysis;
using AskLucy.Domain.Ai;
using AskLucy.Domain.OperationalFailures;

namespace AskLucy.Application.OperationalFailures;

// specs/074 contracts/admin-operational-failures.md → Shapes. Enums serialize as strings API-wide.

public enum UserRefStatus
{
    Active,
    Deleted,
    Erased,
}

/// <summary>A user as the trail shows them. An erased user has no id, name or email left.</summary>
public sealed record UserRefDto(string? Id, string? DisplayName, string? Email, UserRefStatus Status)
{
    public static UserRefDto Erased { get; } = new(null, null, null, UserRefStatus.Erased);
}

public sealed record IncidentSubjectDto(string Type, Guid Id, string? Label, bool Deleted);

public record IncidentSummaryDto
{
    public required Guid Id { get; init; }

    /// <summary>Base64; sent back with a transition as its concurrency token.</summary>
    public required string RowVersion { get; init; }

    public required OperationalFailureSeverity Severity { get; init; }

    public required OperationalFailureEngine Engine { get; init; }

    public required string Operation { get; init; }

    public required OperationalFailureKind Kind { get; init; }

    public Guid? ProviderId { get; init; }

    public string? ProviderName { get; init; }

    public string? Model { get; init; }

    public IncidentSubjectDto? Subject { get; init; }

    public required DateTime FirstSeenUtc { get; init; }

    public required DateTime LastSeenUtc { get; init; }

    public required int OccurrenceCount { get; init; }

    /// <summary>Below <see cref="OccurrenceCount"/> once the per-incident cap is reached (FR-022).</summary>
    public required int StoredOccurrenceCount { get; init; }

    public required int DistinctUserCount { get; init; }

    public required int DistinctSourceCount { get; init; }

    public required int RecoveryCount { get; init; }

    public required string LatestReason { get; init; }

    public required string LatestCorrelationId { get; init; }

    public required IncidentTriageState State { get; init; }

    public required string RootCauseKey { get; init; }

    /// <summary>Other unresolved incidents sharing <see cref="RootCauseKey"/> (FR-026b).</summary>
    public required int RelatedOpenCount { get; init; }

    public required bool IsRecurrence { get; init; }
}

public sealed record IncidentAcknowledgementDto(UserRefDto By, DateTime AtUtc);

public sealed record IncidentResolutionDto(UserRefDto By, DateTime AtUtc, string? Note);

public sealed record CorrectiveActionDto(string Text, string? AdminRoute, CorrectiveAdminAction? AdminAction);

public sealed record ProviderHealthDto(ProviderHealthStatus Status, AiProviderFailureKind? FailureKind, DateTime? CheckedAtUtc);

public sealed record IncidentDetailDto : IncidentSummaryDto
{
    /// <summary>Starts from the summary; the caller's initializer supplies the detail members.</summary>
    [SetsRequiredMembers]
    public IncidentDetailDto(IncidentSummaryDto summary)
        : base(summary)
    {
    }

    public Guid? RecurrenceOfIncidentId { get; init; }

    public IncidentAcknowledgementDto? Acknowledged { get; init; }

    public IncidentResolutionDto? Resolved { get; init; }

    public required CorrectiveActionDto CorrectiveAction { get; init; }

    /// <summary>The AI provider's latest health check (FR-017); null when the incident names no AI provider.</summary>
    public ProviderHealthDto? ProviderHealth { get; init; }

    /// <summary>Up to ten of the most recent distinct users.</summary>
    public required IReadOnlyList<UserRefDto> SampleUsers { get; init; }

    public required bool CanManage { get; init; }

    public required bool CanViewContent { get; init; }
}

public sealed record OccurrenceChatDto(Guid Id, string? Title, bool Deleted);

public sealed record OccurrenceWorkflowDto(Guid Id, string? Name, Guid? ExecutionId, Guid? NodeId, bool Deleted);

public sealed record OccurrenceDocumentDto(Guid Id, string? Name, Guid? KnowledgeBaseId, bool Deleted);

public sealed record OccurrenceAgentDto(Guid Id, string? Name, Guid? ExecutionId, bool Deleted);

public sealed record OccurrenceMcpServerDto(Guid Id, string? Name, bool Deleted);

public sealed record OccurrenceDto(
    Guid Id,
    DateTime OccurredAtUtc,
    OperationalFailureSeverity Severity,
    OperationalFailureKind Kind,
    string Reason,
    string CorrelationId,
    bool IsFailover,
    UserRefDto? User,
    OccurrenceChatDto? Chat,
    Guid? MessageId,
    OccurrenceWorkflowDto? Workflow,
    OccurrenceDocumentDto? Document,
    OccurrenceAgentDto? Agent,
    OccurrenceMcpServerDto? McpServer,
    string? JobId,
    string? SourceIp);

public sealed record BulkTransitionFailureDto(Guid IncidentId, string Reason);

public sealed record BulkTransitionResultDto(int Attempted, int Succeeded, int Skipped, IReadOnlyList<BulkTransitionFailureDto> Failed);

public sealed record ChatInvestigationChatDto(
    Guid Id, string Title, UserRefDto Owner, DateTime CreatedAtUtc, DateTime LastActivityUtc, int MessageCount, bool Deleted);

/// <summary><see cref="TurnNumber"/> counts the chat's user messages up to the failed reply; null when the reply is unknown.</summary>
public sealed record ChatFailurePointDto(Guid OccurrenceId, int? TurnNumber, DateTime OccurredAtUtc, Guid? MessageId);

public sealed record ChatTranscriptMessageDto(Guid Id, string Role, DateTime CreatedAtUtc, string Content, bool IsFailedTurn);

/// <summary><see cref="Transcript"/> is null unless the caller holds the content permission (FR-016b).</summary>
public sealed record ChatInvestigationDto(
    ChatInvestigationChatDto Chat, IReadOnlyList<ChatFailurePointDto> FailurePoints, IReadOnlyList<ChatTranscriptMessageDto>? Transcript);

public sealed record WorkflowRunInvestigationWorkflowDto(Guid Id, string Name, UserRefDto Owner, bool Deleted);

public sealed record WorkflowRunInvestigationRunDto(Guid Id, string Status, DateTime StartedAtUtc, DateTime? FinishedAtUtc, int StepCount);

public sealed record WorkflowRunFailedStepDto(string NodeId, string Name, string Type);

/// <summary><see cref="Input"/> and <see cref="Output"/> are the recorded step JSON, passed through unparsed.</summary>
public sealed record WorkflowRunStepDto(string NodeId, string Name, string Status, object? Input, object? Output, bool IsFailedStep);

public sealed record WorkflowRunInvestigationDto(
    WorkflowRunInvestigationWorkflowDto Workflow,
    WorkflowRunInvestigationRunDto Run,
    WorkflowRunFailedStepDto? FailedStep,
    IReadOnlyList<WorkflowRunStepDto>? Steps);

public sealed record DocumentKnowledgeBaseDto(Guid Id, string Name);

public sealed record DocumentInvestigationDocumentDto(
    Guid Id,
    string Name,
    string ContentType,
    long SizeBytes,
    DocumentKnowledgeBaseDto? KnowledgeBase,
    string ProcessingStatus,
    UserRefDto Owner,
    bool Deleted);

/// <summary><see cref="ExtractedText"/> is null unless the caller holds the content permission, and truncated to 200 KB.</summary>
public sealed record DocumentInvestigationDto(DocumentInvestigationDocumentDto Document, string? ExtractedText, bool ExtractedTextTruncated);
