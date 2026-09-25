using AskLucy.Domain.Common;
using AskLucy.Domain.OperationalFailures;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.OperationalFailures;

/// <summary>
/// Triage transitions (specs/074 data-model). "A new occurrence never changes TriageState" is a
/// property of the store's set-based append, not of this aggregate, so it is asserted by
/// OperationalFailureStoreTests against real SQL Server.
/// </summary>
public sealed class OperationalFailureIncidentTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);

    private static OperationalFailureIncident NewIncident(Guid? recurrenceOf = null) =>
        OperationalFailureIncident.Open(
            "grouping", "root", OperationalFailureEngine.Voice, "tts", OperationalFailureKind.CredentialRejected,
            OperationalFailureSeverity.Critical, Now, "ElevenLabs rejected the credential.", "corr-1",
            providerName: "ElevenLabs", recurrenceOfIncidentId: recurrenceOf);

    [Fact]
    public void Open_StartsOpenWithZeroCounters()
    {
        var incident = NewIncident();

        incident.Id.Should().NotBeEmpty();
        incident.TriageState.Should().Be(IncidentTriageState.Open);
        incident.OccurrenceCount.Should().Be(0);
        incident.StoredOccurrenceCount.Should().Be(0);
        incident.FirstSeenUtc.Should().Be(Now);
        incident.LastSeenUtc.Should().Be(Now);
        incident.IsRecurrence.Should().BeFalse();
    }

    [Fact]
    public void Open_WithEarlierIncident_IsRecurrence()
    {
        NewIncident(Guid.NewGuid()).IsRecurrence.Should().BeTrue();
    }

    [Fact]
    public void Acknowledge_ThenResolve_RecordsBothActors()
    {
        var incident = NewIncident();

        incident.Acknowledge("admin-1", Now.AddMinutes(1)).Should().Be(IncidentTransitionResult.Applied);
        incident.Resolve("admin-2", Now.AddMinutes(2), "  Rotated the key.  ").Should().Be(IncidentTransitionResult.Applied);

        incident.TriageState.Should().Be(IncidentTriageState.Resolved);
        incident.AcknowledgedByUserId.Should().Be("admin-1");
        incident.AcknowledgedAtUtc.Should().Be(Now.AddMinutes(1));
        incident.ResolvedByUserId.Should().Be("admin-2");
        incident.ResolvedAtUtc.Should().Be(Now.AddMinutes(2));
        incident.ResolutionNote.Should().Be("Rotated the key.");
    }

    [Fact]
    public void Resolve_FromOpen_Applies()
    {
        var incident = NewIncident();

        incident.Resolve("admin-1", Now, note: null).Should().Be(IncidentTransitionResult.Applied);

        incident.TriageState.Should().Be(IncidentTriageState.Resolved);
        incident.ResolutionNote.Should().BeNull();
    }

    [Fact]
    public void Reopen_FromResolved_ClearsAcknowledgeAndResolveFields()
    {
        var incident = NewIncident();
        incident.Acknowledge("admin-1", Now);
        incident.Resolve("admin-1", Now, "note");

        incident.Reopen().Should().Be(IncidentTransitionResult.Applied);

        incident.TriageState.Should().Be(IncidentTriageState.Open);
        incident.AcknowledgedByUserId.Should().BeNull();
        incident.AcknowledgedAtUtc.Should().BeNull();
        incident.ResolvedByUserId.Should().BeNull();
        incident.ResolvedAtUtc.Should().BeNull();
        incident.ResolutionNote.Should().BeNull();
    }

    [Fact]
    public void Reopen_WhenNotResolved_IsAlreadyInState()
    {
        NewIncident().Reopen().Should().Be(IncidentTransitionResult.AlreadyInState);
    }

    [Fact]
    public void Acknowledge_WhenAcknowledged_IsAlreadyInState()
    {
        var incident = NewIncident();
        incident.Acknowledge("admin-1", Now);

        incident.Acknowledge("admin-2", Now.AddMinutes(1)).Should().Be(IncidentTransitionResult.AlreadyInState);
        incident.AcknowledgedByUserId.Should().Be("admin-1");
    }

    [Fact]
    public void Acknowledge_WhenResolved_IsAlreadyInState()
    {
        var incident = NewIncident();
        incident.Resolve("admin-1", Now, null);

        incident.Acknowledge("admin-2", Now).Should().Be(IncidentTransitionResult.AlreadyInState);
        incident.TriageState.Should().Be(IncidentTriageState.Resolved);
    }

    [Fact]
    public void Resolve_WhenResolved_IsAlreadyInState()
    {
        var incident = NewIncident();
        incident.Resolve("admin-1", Now, "first");

        incident.Resolve("admin-2", Now.AddMinutes(1), "second").Should().Be(IncidentTransitionResult.AlreadyInState);
        incident.ResolutionNote.Should().Be("first");
    }

    [Fact]
    public void Resolve_NoteAtLimit_IsAccepted()
    {
        var incident = NewIncident();

        incident.Resolve("admin-1", Now, new string('a', OperationalFailureIncident.MaxResolutionNoteLength))
            .Should().Be(IncidentTransitionResult.Applied);
    }

    [Fact]
    public void Resolve_NoteOverLimit_Throws()
    {
        var incident = NewIncident();

        var act = () => incident.Resolve("admin-1", Now, new string('a', OperationalFailureIncident.MaxResolutionNoteLength + 1));

        act.Should().Throw<DomainRuleViolationException>();
        incident.TriageState.Should().Be(IncidentTriageState.Open);
    }
}
