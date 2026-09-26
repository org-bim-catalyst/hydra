using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.OperationalFailures;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AskLucy.Persistence.Tests.OperationalFailures;

/// <summary>
/// specs/074 T062 (US2) — grouping through the real ingestor and store: the key decides which
/// incident an occurrence joins, triage state survives new occurrences, and a resolved incident
/// is never reopened by one. Each test uses its own provider name, so its keys never collide
/// with another run's.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class OperationalFailureGroupingTests(PersistenceTestFixture fixture)
{
    private static readonly DateTime T0 = new(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _provider = $"ElevenLabs-{Guid.NewGuid():N}";

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task TheSameKeyAcrossThreeUsers_ShouldBeOneIncident()
    {
        await IngestAsync(Report(T0, userId: "user-a"), Report(T0.AddSeconds(1), userId: "user-b"), Report(T0.AddSeconds(2), userId: "user-c"));

        var incident = (await IncidentsAsync()).Should().ContainSingle().Subject;
        incident.OccurrenceCount.Should().Be(3);
        incident.DistinctUserCount.Should().Be(3);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ANewOccurrence_ShouldLeaveAnAcknowledgedIncidentAcknowledged()
    {
        await IngestAsync(Report(T0));
        await TransitionAsync(i => i.Acknowledge("admin-074", T0.AddMinutes(1)));

        await IngestAsync(Report(T0.AddMinutes(2)));

        var incident = (await IncidentsAsync()).Should().ContainSingle().Subject;
        incident.TriageState.Should().Be(IncidentTriageState.Acknowledged, "a new occurrence does not re-flag an acknowledged incident");
        incident.OccurrenceCount.Should().Be(2);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ANewOccurrenceAfterResolve_ShouldOpenARecurrence()
    {
        await IngestAsync(Report(T0));
        var original = await TransitionAsync(i => i.Resolve("admin-074", T0.AddMinutes(1), "rotated the key"));

        await IngestAsync(Report(T0.AddMinutes(2)));

        var incidents = await IncidentsAsync();
        incidents.Should().HaveCount(2);
        var recurrence = incidents.Single(i => i.Id != original);
        recurrence.RecurrenceOfIncidentId.Should().Be(original);
        recurrence.TriageState.Should().Be(IncidentTriageState.Open);
        incidents.Single(i => i.Id == original).TriageState.Should().Be(IncidentTriageState.Resolved);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ARecoveryWithNoOpenIncident_ShouldChangeNothing()
    {
        await IngestAsync(Recovery());
        (await IncidentsAsync()).Should().BeEmpty();

        await IngestAsync(Report(T0));
        await TransitionAsync(i => i.Resolve("admin-074", T0.AddMinutes(1), null));
        await IngestAsync(Recovery());

        (await IncidentsAsync()).Should().ContainSingle().Which.RecoveryCount.Should().Be(0, "a resolved incident takes no recoveries");
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ARecovery_ShouldCountAgainstTheIncidentItsFailoverJoined()
    {
        await IngestAsync(Report(T0), Recovery(), Report(T0.AddSeconds(5)), Recovery());

        var incident = (await IncidentsAsync()).Should().ContainSingle().Subject;
        incident.OccurrenceCount.Should().Be(2);
        incident.RecoveryCount.Should().Be(2);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task TheSameKindOnADifferentModel_ShouldBeTwoIncidents()
    {
        await IngestAsync(Report(T0, model: "eleven_flash_v2_5"), Report(T0.AddSeconds(1), model: "eleven_multilingual_v2"));

        (await IncidentsAsync()).Should().HaveCount(2);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task TheSameKindOnADifferentOperation_ShouldBeTwoIncidents()
    {
        await IngestAsync(Report(T0, operation: "Text-to-speech"), Report(T0.AddSeconds(1), operation: "Transcription"));

        (await IncidentsAsync()).Should().HaveCount(2);
    }

    private async Task IngestAsync(params OperationalFailureSignal[] signals)
    {
        await using var dbContext = fixture.CreateDbContext();
        var ingestor = new OperationalFailureIngestor(
            new OperationalFailureStore(dbContext, Options.Create(new OperationalFailuresOptions())),
            Substitute.For<IPublisher>(),
            NullLogger<OperationalFailureIngestor>.Instance);

        // One signal per call, as the writer would drain them over time.
        foreach (var signal in signals)
        {
            await ingestor.IngestAsync([signal], TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Applies a triage transition to this test's one unresolved incident; returns its id.</summary>
    private async Task<Guid> TransitionAsync(Action<OperationalFailureIncident> transition)
    {
        await using var dbContext = fixture.CreateDbContext();
        var incident = await dbContext.OperationalFailureIncidents
            .SingleAsync(i => i.ProviderName == _provider && i.TriageState != IncidentTriageState.Resolved, TestContext.Current.CancellationToken);
        transition(incident);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return incident.Id;
    }

    private async Task<List<OperationalFailureIncident>> IncidentsAsync()
    {
        await using var dbContext = fixture.CreateDbContext();
        return await dbContext.OperationalFailureIncidents.AsNoTracking()
            .Where(i => i.ProviderName == _provider)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private OperationalFailureReport Report(
        DateTime at, string? userId = "user-a", string model = "eleven_flash_v2_5", string operation = "Text-to-speech") =>
        new()
        {
            Engine = OperationalFailureEngine.Voice,
            Operation = operation,
            Kind = OperationalFailureKind.CredentialRejected,
            Outcome = OperationalFailureOutcome.DegradedServed,
            Reason = "The voice provider rejected the credential.",
            ProviderName = _provider,
            Model = model,
            References = new OperationalFailureReferences { UserId = userId },
            IsFailover = true,
            OccurredAtUtc = at,
            CorrelationId = Guid.NewGuid().ToString("N"),
        };

    private VoiceRecoveryReport Recovery() =>
        new()
        {
            Operation = "Text-to-speech",
            Kind = OperationalFailureKind.CredentialRejected,
            ProviderName = _provider,
            Model = "eleven_flash_v2_5",
            UserId = "user-a",
        };
}
