using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.OperationalFailures;
using AskLucy.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace AskLucy.Persistence.Tests.OperationalFailures;

/// <summary>
/// specs/074 T068 — the list filters, stable paging, the nav badge count, the related list and the
/// reopen collision, against real SQL Server. Every test scopes itself by a provider name or root
/// cause unique to the run, so the shared database's other incidents never leak into an assertion.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class OperationalFailureTriageQueryTests(PersistenceTestFixture fixture)
{
    private static readonly DateTime T0 = new(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _provider = $"triage-{Guid.NewGuid():N}";

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task List_ShouldApplyEachFilter_AloneAndCombined_OrIngValuesWithinOne()
    {
        var voiceCritical = await AppendAsync(Request(NewKey(), T0, OperationalFailureSeverity.Critical, OperationalFailureEngine.Voice, OperationalFailureKind.CredentialRejected));
        var chatError = await AppendAsync(Request(NewKey(), T0.AddMinutes(1), OperationalFailureSeverity.Error, OperationalFailureEngine.Chat, OperationalFailureKind.QuotaExhausted));
        var docsWarning = await AppendAsync(Request(NewKey(), T0.AddMinutes(2), OperationalFailureSeverity.Warning, OperationalFailureEngine.DocumentProcessing, OperationalFailureKind.CredentialRejected));

        (await ListIdsAsync(Filter() with { Severities = [OperationalFailureSeverity.Critical] }))
            .Should().Equal(voiceCritical.IncidentId);
        (await ListIdsAsync(Filter() with { Engines = [OperationalFailureEngine.Chat] }))
            .Should().Equal(chatError.IncidentId);
        (await ListIdsAsync(Filter() with { Kinds = [OperationalFailureKind.CredentialRejected] }))
            .Should().BeEquivalentTo([voiceCritical.IncidentId, docsWarning.IncidentId]);

        (await ListIdsAsync(Filter() with { Severities = [OperationalFailureSeverity.Critical, OperationalFailureSeverity.Warning] }))
            .Should().BeEquivalentTo([voiceCritical.IncidentId, docsWarning.IncidentId], "values within one filter are OR-ed");

        (await ListIdsAsync(Filter() with
        {
            Kinds = [OperationalFailureKind.CredentialRejected],
            Engines = [OperationalFailureEngine.DocumentProcessing, OperationalFailureEngine.Chat],
        })).Should().Equal([docsWarning.IncidentId], "separate filters are AND-ed");

        (await ListIdsAsync(Filter() with { FromUtc = T0.AddSeconds(30) }))
            .Should().BeEquivalentTo([chatError.IncidentId, docsWarning.IncidentId], "the time range applies to LastSeenUtc");
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task List_ShouldPageStably_WhenLastSeenTies()
    {
        for (var i = 0; i < 7; i++)
        {
            await AppendAsync(Request(NewKey(), T0));
        }

        var pages = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            await using var dbContext = fixture.CreateDbContext();
            var (items, total) = await Store(dbContext).ListIncidentsAsync(Filter(), page, 3, TestContext.Current.CancellationToken);
            total.Should().Be(7);
            pages.AddRange(items.Select(i => i.Id));
        }

        pages.Should().HaveCount(7).And.OnlyHaveUniqueItems("equal LastSeenUtc values are tie-broken, so no row repeats or goes missing across pages");
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Badge_ShouldCountRootCauses_NotIncidents_AndOnlyOpenCriticalOnes()
    {
        var baseline = await BadgeAsync();
        var burstCause = NewKey();

        var burst = new List<Guid>();
        for (var i = 0; i < 40; i++)
        {
            burst.Add((await AppendAsync(Request(NewKey(), T0.AddSeconds(i), OperationalFailureSeverity.Critical, rootCauseKey: burstCause))).IncidentId);
        }

        await AppendAsync(Request(NewKey(), T0, OperationalFailureSeverity.Critical));
        (await BadgeAsync()).Should().Be(baseline + 2, "40 incidents sharing one root cause are one badge entry");

        await AppendAsync(Request(NewKey(), T0, OperationalFailureSeverity.Warning));
        (await BadgeAsync()).Should().Be(baseline + 2, "a Warning is never counted");

        var outcomes = await TransitionAsync(burst, i => i.Acknowledge("admin-074", T0.AddHours(1)));
        outcomes.Should().OnlyContain(o => o.Status == IncidentTransitionStatus.Applied);
        (await BadgeAsync()).Should().Be(baseline + 1, "an acknowledged incident is no longer unacknowledged");
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Related_ShouldExcludeTheIncidentItself_AndResolvedOnes()
    {
        var cause = NewKey();
        var self = await AppendAsync(Request(NewKey(), T0, rootCauseKey: cause));
        var sibling = await AppendAsync(Request(NewKey(), T0.AddMinutes(1), rootCauseKey: cause));
        var resolved = await AppendAsync(Request(NewKey(), T0.AddMinutes(2), rootCauseKey: cause));
        await TransitionAsync([resolved.IncidentId], i => i.Resolve("admin-074", T0.AddHours(1), null));

        await using var dbContext = fixture.CreateDbContext();
        var related = await Store(dbContext).ListRelatedAsync(self.IncidentId, 1, 25, TestContext.Current.CancellationToken);

        related.Should().NotBeNull();
        related!.Value.TotalCount.Should().Be(1);
        related.Value.Items.Select(i => i.Id).Should().Equal(sibling.IncidentId);

        (await Store(dbContext).ListRelatedAsync(Guid.NewGuid(), 1, 25, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Reopen_WhileANewerIncidentHoldsTheKey_ShouldReportTheNewerOne()
    {
        var key = NewKey();
        var original = await AppendAsync(Request(key, T0));
        await TransitionAsync([original.IncidentId], i => i.Resolve("admin-074", T0.AddMinutes(1), "fixed"));
        var recurrence = await AppendAsync(Request(key, T0.AddMinutes(2)));
        recurrence.Opened.Should().BeTrue();

        var outcome = (await TransitionAsync([original.IncidentId], i => i.Reopen())).Single();

        outcome.Status.Should().Be(IncidentTransitionStatus.NewerIncidentOpen);
        outcome.NewerIncidentId.Should().Be(recurrence.IncidentId);
    }

    private IncidentFilter Filter() => new(T0.AddDays(-1), T0.AddDays(1), IncidentStateFilter.Unresolved, Provider: _provider);

    private async Task<IReadOnlyList<Guid>> ListIdsAsync(IncidentFilter filter)
    {
        await using var dbContext = fixture.CreateDbContext();
        var (items, _) = await Store(dbContext).ListIncidentsAsync(filter, 1, 100, TestContext.Current.CancellationToken);
        return [.. items.Select(i => i.Id)];
    }

    private async Task<int> BadgeAsync()
    {
        await using var dbContext = fixture.CreateDbContext();
        return await Store(dbContext).CountUnacknowledgedCriticalRootCausesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<IReadOnlyList<IncidentTransitionOutcome>> TransitionAsync(
        IReadOnlyCollection<Guid> incidentIds, Func<OperationalFailureIncident, IncidentTransitionResult> transition)
    {
        await using var dbContext = fixture.CreateDbContext();
        return await Store(dbContext).TransitionAsync(incidentIds, transition, TestContext.Current.CancellationToken);
    }

    private async Task<IncidentAppendResult> AppendAsync(IncidentAppendRequest request)
    {
        await using var dbContext = fixture.CreateDbContext();
        return await Store(dbContext).AppendAsync(request, TestContext.Current.CancellationToken);
    }

    private static OperationalFailureStore Store(AskLucyDbContext dbContext) =>
        new(dbContext, Options.Create(new OperationalFailuresOptions()));

    /// <summary>64 hex characters, like a real SHA-256 key, and unique per test so runs never collide.</summary>
    private static string NewKey() => $"{Guid.NewGuid():N}{Guid.NewGuid():N}";

    private IncidentAppendRequest Request(
        string key,
        DateTime at,
        OperationalFailureSeverity severity = OperationalFailureSeverity.Error,
        OperationalFailureEngine engine = OperationalFailureEngine.Voice,
        OperationalFailureKind kind = OperationalFailureKind.CredentialRejected,
        string? rootCauseKey = null) =>
        new(
            key,
            rootCauseKey ?? key,
            engine,
            "triage.test",
            kind,
            severity,
            at,
            "The provider rejected the credential.",
            Guid.NewGuid().ToString("N"),
            ProviderId: null,
            ProviderName: _provider,
            Model: null,
            Subject: null,
            References: new OperationalFailureReferences(),
            SourceIp: null,
            IsFailover: false);
}
