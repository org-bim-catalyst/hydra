using AskLucy.Domain.OperationalFailures;

namespace AskLucy.Application.OperationalFailures.Abstractions;

/// <summary>
/// The specific thing that failed and is part of the grouping key (FR-018): a workflow definition,
/// agent, document or MCP server. Chats, users, runs and job instances are occurrence references,
/// never a subject. <see cref="Label"/> is display only.
/// </summary>
public sealed record OperationalFailureSubject(string Type, Guid Id, string? Label = null);

/// <summary>
/// One failure handed to <see cref="IOperationalFailureRecorder.Record"/> (data-model.md →
/// Application-level types). Deliberately has no severity (derived, research D5) and no field for
/// request or response bodies, prompts, file names, passwords, codes or tokens (FR-012, FR-012a).
/// </summary>
public sealed record OperationalFailureReport : OperationalFailureSignal
{
    public required OperationalFailureEngine Engine { get; init; }

    /// <summary>What was being done, in system words: "Chat reply", "Text-to-speech", a step or job type.</summary>
    public required string Operation { get; init; }

    public required OperationalFailureKind Kind { get; init; }

    public OperationalFailureOutcome Outcome { get; init; } = OperationalFailureOutcome.Failed;

    /// <summary>System prose only — never a vendor message (FR-011). Sanitised again at ingestion.</summary>
    public required string Reason { get; init; }

    /// <summary>Only its type name is ever used, as the fallback reason.</summary>
    public Exception? Exception { get; init; }

    public Guid? ProviderId { get; init; }

    public string? ProviderName { get; init; }

    public string? Model { get; init; }

    public OperationalFailureSubject? Subject { get; init; }

    public OperationalFailureReferences References { get; init; } = OperationalFailureReferences.None;

    /// <summary>Access engine only, and only for a matched account (FR-012a).</summary>
    public string? SourceIp { get; init; }

    public bool IsFailover { get; init; }
}

/// <summary>
/// A voice provider recovered after a failover. Increments <c>RecoveryCount</c> on the open
/// incident with the same key as the failover report built from the same inputs; ignored when
/// none is open (spec Edge Cases).
/// </summary>
public sealed record VoiceRecoveryReport : OperationalFailureSignal
{
    public required string Operation { get; init; }

    public required OperationalFailureKind Kind { get; init; }

    public string? ProviderName { get; init; }

    public string? Model { get; init; }

    public string? UserId { get; init; }
}
