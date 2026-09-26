using AskLucy.Application.Abstractions;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.OperationalFailures;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AskLucy.Persistence.Tests.OperationalFailures;

/// <summary>
/// specs/074 T015 (research D3). The filtered unique index, the set-based counters and the
/// <c>INSERT … WHERE NOT EXISTS</c> participant write only mean anything against real SQL Server.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class OperationalFailureStoreTests(PersistenceTestFixture fixture)
{
    private static readonly DateTime T0 = new(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Append_ShouldOpenThenJoin_KeepingTheLatestAndTheHighest()
    {
        var key = NewKey();

        var first = await AppendAsync(Request(key, T0.AddSeconds(10), OperationalFailureSeverity.Critical, reason: "later"));
        var second = await AppendAsync(Request(key, T0, OperationalFailureSeverity.Warning, reason: "earlier"));

        first.Opened.Should().BeTrue();
        second.Opened.Should().BeFalse();
        second.IncidentId.Should().Be(first.IncidentId);
        second.Severity.Should().Be(OperationalFailureSeverity.Critical);

        var incident = await LoadAsync(first.IncidentId);
        incident.OccurrenceCount.Should().Be(2);
        incident.LastSeenUtc.Should().Be(T0.AddSeconds(10), "an out-of-order occurrence never moves LastSeenUtc back");
        incident.LatestReason.Should().Be("later");
        incident.HighestSeverity.Should().Be(OperationalFailureSeverity.Critical);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Append_ShouldRaiseHighestSeverity_FromErrorToCritical()
    {
        var key = NewKey();

        var opened = await AppendAsync(Request(key, T0, OperationalFailureSeverity.Error));
        await AppendAsync(Request(key, T0.AddSeconds(1), OperationalFailureSeverity.Warning));
        (await LoadAsync(opened.IncidentId)).HighestSeverity.Should().Be(OperationalFailureSeverity.Error);

        await AppendAsync(Request(key, T0.AddSeconds(2), OperationalFailureSeverity.Critical));
        (await LoadAsync(opened.IncidentId)).HighestSeverity.Should().Be(OperationalFailureSeverity.Critical);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ConcurrentAppendsOfOneKey_ShouldProduceExactlyOneIncident()
    {
        var key = NewKey();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(
            () => AppendAsync(Request(key, T0.AddMilliseconds(i))),
            TestContext.Current.CancellationToken)));

        results.Select(r => r.IncidentId).Distinct().Should().ContainSingle();
        results.Count(r => r.Opened).Should().Be(1);

        await using var dbContext = fixture.CreateDbContext();
        var incidents = await dbContext.OperationalFailureIncidents.AsNoTracking()
            .Where(i => i.GroupingKey == key)
            .ToListAsync(TestContext.Current.CancellationToken);
        incidents.Should().ContainSingle().Which.OccurrenceCount.Should().Be(20);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Append_AfterResolve_ShouldOpenARecurrence()
    {
        var key = NewKey();
        var original = await AppendAsync(Request(key, T0));

        await using (var dbContext = fixture.CreateDbContext())
        {
            var incident = await dbContext.OperationalFailureIncidents.SingleAsync(i => i.Id == original.IncidentId, TestContext.Current.CancellationToken);
            incident.Resolve("admin-074", T0.AddMinutes(1), "fixed");
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var recurrence = await AppendAsync(Request(key, T0.AddMinutes(2)));

        recurrence.Opened.Should().BeTrue();
        recurrence.IncidentId.Should().NotBe(original.IncidentId);
        (await LoadAsync(recurrence.IncidentId)).RecurrenceOfIncidentId.Should().Be(original.IncidentId);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Append_BeyondTheCap_ShouldCountButNotStore()
    {
        var key = NewKey();
        IncidentAppendResult? result = null;
        for (var i = 0; i < 5; i++)
        {
            result = await AppendAsync(Request(key, T0.AddSeconds(i)), maxStoredOccurrences: 3);
        }

        var incident = await LoadAsync(result!.IncidentId);
        incident.OccurrenceCount.Should().Be(5);
        incident.StoredOccurrenceCount.Should().Be(3);

        await using var dbContext = fixture.CreateDbContext();
        (await dbContext.OperationalFailureOccurrences.CountAsync(o => o.IncidentId == incident.Id, TestContext.Current.CancellationToken))
            .Should().Be(3);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Participants_ShouldCountDistinctUsersAndSources_EvenBeyondTheCap()
    {
        var key = NewKey();
        IncidentAppendResult? result = null;
        string[] users = ["user-a", "user-a", "user-b", "user-a", "user-b"];
        for (var i = 0; i < users.Length; i++)
        {
            result = await AppendAsync(Request(key, T0.AddSeconds(i), userId: users[i], sourceIp: "10.0.0.1"), maxStoredOccurrences: 1);
        }

        var incident = await LoadAsync(result!.IncidentId);
        incident.DistinctUserCount.Should().Be(2, "user-b first appeared after the stored-occurrence cap was spent");
        incident.DistinctSourceCount.Should().Be(1);
        incident.StoredOccurrenceCount.Should().Be(1);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Append_ShouldStampCreationOnly_LeavingModifiedUntouchedByBookkeeping()
    {
        var key = NewKey();

        var opened = await AppendAsync(Request(key, T0), audited: true);
        await AppendAsync(Request(key, T0.AddSeconds(1)), audited: true);

        var incident = await LoadAsync(opened.IncidentId);
        incident.CreatedBy.Should().Be("system");
        incident.ModifiedAtUtc.Should().BeNull("joining an incident is a bookkeeping write, not an edit");
        incident.LastSeenUtc.Should().Be(T0.AddSeconds(1));
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task IncrementRecovery_ShouldOnlyCountAgainstAnUnresolvedIncident()
    {
        var key = NewKey();

        await using (var dbContext = fixture.CreateDbContext())
        {
            (await Store(dbContext).IncrementRecoveryAsync(key, TestContext.Current.CancellationToken)).Should().BeFalse();
        }

        var opened = await AppendAsync(Request(key, T0));
        await using (var dbContext = fixture.CreateDbContext())
        {
            (await Store(dbContext).IncrementRecoveryAsync(key, TestContext.Current.CancellationToken)).Should().BeTrue();
        }

        (await LoadAsync(opened.IncidentId)).RecoveryCount.Should().Be(1);
    }

    private async Task<IncidentAppendResult> AppendAsync(IncidentAppendRequest request, int? maxStoredOccurrences = null, bool audited = false)
    {
        await using var dbContext = audited
            ? fixture.CreateAuditedDbContext(BackgroundWriter(), Substitute.For<ICorrelationIdAccessor>())
            : fixture.CreateDbContext();
        return await Store(dbContext, maxStoredOccurrences).AppendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>The recorder writes from its own scope, which has no signed-in user; a bare substitute would say <c>""</c>, not null.</summary>
    private static ICurrentUserAccessor BackgroundWriter()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns((string?)null);
        return currentUser;
    }

    private static OperationalFailureStore Store(AskLucyDbContext dbContext, int? maxStoredOccurrences = null) =>
        new(dbContext, Options.Create(maxStoredOccurrences is { } cap
            ? new OperationalFailuresOptions { MaxStoredOccurrencesPerIncident = cap }
            : new OperationalFailuresOptions()));

    private async Task<OperationalFailureIncident> LoadAsync(Guid incidentId)
    {
        await using var dbContext = fixture.CreateDbContext();
        return await dbContext.OperationalFailureIncidents.AsNoTracking()
            .SingleAsync(i => i.Id == incidentId, TestContext.Current.CancellationToken);
    }

    /// <summary>64 hex characters, like a real SHA-256 key, and unique per test so runs never collide.</summary>
    private static string NewKey() => $"{Guid.NewGuid():N}{Guid.NewGuid():N}";

    private static IncidentAppendRequest Request(
        string key,
        DateTime at,
        OperationalFailureSeverity severity = OperationalFailureSeverity.Error,
        string reason = "The provider rejected the credential.",
        string? userId = null,
        string? sourceIp = null) =>
        new(
            key,
            key,
            OperationalFailureEngine.Voice,
            "voice.synthesize",
            OperationalFailureKind.CredentialRejected,
            severity,
            at,
            reason,
            Guid.NewGuid().ToString("N"),
            ProviderId: null,
            ProviderName: "ElevenLabs",
            Model: "eleven_multilingual_v2",
            Subject: null,
            References: new OperationalFailureReferences { UserId = userId },
            SourceIp: sourceIp,
            IsFailover: true);
}
