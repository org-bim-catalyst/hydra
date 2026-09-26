using AskLucy.Application.Abstractions;
using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.OperationalFailures.Commands.AcknowledgeIncident;
using AskLucy.Application.OperationalFailures.Commands.AcknowledgeRootCause;
using AskLucy.Application.OperationalFailures.Commands.ReopenIncident;
using AskLucy.Application.OperationalFailures.Commands.ResolveIncident;
using AskLucy.Application.OperationalFailures.Commands.ResolveRootCause;
using AskLucy.Application.OperationalFailures.Queries.GetSummary;
using AskLucy.Application.OperationalFailures.Queries.ListRelatedIncidents;
using AskLucy.Domain.OperationalFailures;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;
using static AskLucy.Application.Tests.OperationalFailures.OperationalFailureTestData;

namespace AskLucy.Application.Tests.OperationalFailures;

/// <summary>
/// specs/074 US3 (T067) — acknowledge, resolve and reopen, one at a time and per root cause.
/// Conflicts are judged on state (research D19): a transition whose precondition no longer holds
/// is someone else's change, and surfaces as <see cref="IncidentConflictException"/>.
/// </summary>
public sealed class IncidentTransitionTests
{
    private readonly IOperationalFailureStore _store = Substitute.For<IOperationalFailureStore>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly Dictionary<Guid, OperationalFailureIncident> _incidents = [];
    private readonly Dictionary<Guid, IncidentTransitionOutcome> _forcedOutcomes = [];

    public IncidentTransitionTests()
    {
        _currentUser.UserId.Returns("admin-1");

        // Applies the transition to the in-memory incidents, as the store does to the tracked aggregate.
        _store.TransitionAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<Func<OperationalFailureIncident, IncidentTransitionResult>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var ids = call.Arg<IReadOnlyCollection<Guid>>()!;
                var transition = call.Arg<Func<OperationalFailureIncident, IncidentTransitionResult>>()!;
                return ids.Select(id => _forcedOutcomes.TryGetValue(id, out var forced) ? forced
                    : _incidents.TryGetValue(id, out var incident)
                        ? new IncidentTransitionOutcome(id, transition(incident) == IncidentTransitionResult.Applied
                            ? IncidentTransitionStatus.Applied
                            : IncidentTransitionStatus.AlreadyInState)
                        : new IncidentTransitionOutcome(id, IncidentTransitionStatus.NotFound)).ToList();
            });
    }

    private IncidentTriageService Triage() => new(_store, new FakeTimeProvider(Now), _currentUser);

    private OperationalFailureIncident Given(string rootCauseKey = "root-1")
    {
        var incident = Incident(rootCauseKey: rootCauseKey);
        _incidents[incident.Id] = incident;
        return incident;
    }

    private void GivenRootCause(string rootCauseKey, IEnumerable<OperationalFailureIncident> incidents) =>
        _store.ListUnresolvedIdsByRootCauseAsync(rootCauseKey, Arg.Any<CancellationToken>())
            .Returns(incidents.Select(i => i.Id).ToList());

    [Fact]
    public async Task Acknowledge_RecordsWhoAndWhen()
    {
        var incident = Given();

        await new AcknowledgeIncidentCommandHandler(Triage())
            .Handle(new AcknowledgeIncidentCommand(incident.Id), TestContext.Current.CancellationToken);

        incident.TriageState.Should().Be(IncidentTriageState.Acknowledged);
        incident.AcknowledgedByUserId.Should().Be("admin-1");
        incident.AcknowledgedAtUtc.Should().Be(Now);
    }

    [Fact]
    public async Task Resolve_RecordsWhoWhenAndTheNote()
    {
        var incident = Given();

        await new ResolveIncidentCommandHandler(Triage())
            .Handle(new ResolveIncidentCommand(incident.Id, "  Rotated the key.  "), TestContext.Current.CancellationToken);

        incident.TriageState.Should().Be(IncidentTriageState.Resolved);
        incident.ResolvedByUserId.Should().Be("admin-1");
        incident.ResolvedAtUtc.Should().Be(Now);
        incident.ResolutionNote.Should().Be("Rotated the key.");
    }

    [Fact]
    public async Task Reopen_ReturnsAResolvedIncidentToOpen_AndClearsItsTriageFields()
    {
        var incident = Given();
        incident.Acknowledge("someone", Now.AddHours(-2));
        incident.Resolve("someone", Now.AddHours(-1), "done");

        await new ReopenIncidentCommandHandler(Triage())
            .Handle(new ReopenIncidentCommand(incident.Id), TestContext.Current.CancellationToken);

        incident.TriageState.Should().Be(IncidentTriageState.Open);
        incident.AcknowledgedByUserId.Should().BeNull();
        incident.ResolvedByUserId.Should().BeNull();
        incident.ResolutionNote.Should().BeNull();
    }

    [Fact]
    public async Task Acknowledge_WhenSomeoneElseGotThereFirst_IsAConflict()
    {
        var incident = Given();
        incident.Acknowledge("other-admin", Now.AddMinutes(-1));

        var act = () => new AcknowledgeIncidentCommandHandler(Triage())
            .Handle(new AcknowledgeIncidentCommand(incident.Id), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<IncidentConflictException>())
            .Which.NewerIncidentId.Should().BeNull();
        incident.AcknowledgedByUserId.Should().Be("other-admin");
    }

    [Fact]
    public async Task Transition_ThatKeptConflicting_IsAConflict()
    {
        var incident = Given();
        _forcedOutcomes[incident.Id] = new IncidentTransitionOutcome(incident.Id, IncidentTransitionStatus.Conflict);

        var act = () => new ResolveIncidentCommandHandler(Triage())
            .Handle(new ResolveIncidentCommand(incident.Id, null), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<IncidentConflictException>();
    }

    [Fact]
    public async Task Reopen_WhileANewerIncidentHoldsTheKey_IsAConflictNamingTheNewerIncident()
    {
        var incident = Given();
        var newer = Guid.NewGuid();
        _forcedOutcomes[incident.Id] = new IncidentTransitionOutcome(incident.Id, IncidentTransitionStatus.NewerIncidentOpen, newer);

        var act = () => new ReopenIncidentCommandHandler(Triage())
            .Handle(new ReopenIncidentCommand(incident.Id), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<IncidentConflictException>())
            .Which.NewerIncidentId.Should().Be(newer);
    }

    [Fact]
    public async Task Transition_OnAnUnknownIncident_IsNotFound()
    {
        var act = () => new AcknowledgeIncidentCommandHandler(Triage())
            .Handle(new AcknowledgeIncidentCommand(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Transition_WithoutAnActingUser_IsUnauthorized()
    {
        var incident = Given();
        _currentUser.UserId.Returns((string?)null);

        var act = () => new AcknowledgeIncidentCommandHandler(Triage())
            .Handle(new AcknowledgeIncidentCommand(incident.Id), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        incident.TriageState.Should().Be(IncidentTriageState.Open);
    }

    [Fact]
    public async Task ResolveRootCause_ResolvesEveryIncidentSharingIt()
    {
        var incidents = Enumerable.Range(0, 40).Select(_ => Given("openai-key")).ToList();
        GivenRootCause("openai-key", incidents);

        var result = await new ResolveRootCauseCommandHandler(Triage())
            .Handle(new ResolveRootCauseCommand("openai-key", "Rotated."), TestContext.Current.CancellationToken);

        result.Should().BeEquivalentTo(new BulkTransitionResultDto(40, 40, 0, []));
        incidents.Should().OnlyContain(i => i.TriageState == IncidentTriageState.Resolved && i.ResolutionNote == "Rotated.");
    }

    [Fact]
    public async Task ResolveRootCause_SkipsOneAlreadyResolved_AndReportsOneThatKeptChanging()
    {
        var incidents = Enumerable.Range(0, 40).Select(_ => Given("openai-key")).ToList();
        GivenRootCause("openai-key", incidents);
        incidents[0].Resolve("other-admin", Now.AddMinutes(-1), null);
        _forcedOutcomes[incidents[1].Id] = new IncidentTransitionOutcome(incidents[1].Id, IncidentTransitionStatus.Conflict);

        var result = await new ResolveRootCauseCommandHandler(Triage())
            .Handle(new ResolveRootCauseCommand("openai-key", null), TestContext.Current.CancellationToken);

        result.Attempted.Should().Be(40);
        result.Succeeded.Should().Be(38);
        result.Skipped.Should().Be(1);
        result.Failed.Should().ContainSingle().Which.IncidentId.Should().Be(incidents[1].Id);
        incidents.Skip(2).Should().OnlyContain(i => i.TriageState == IncidentTriageState.Resolved);
    }

    [Fact]
    public async Task AcknowledgeRootCause_SkipsIncidentsAlreadyAcknowledged()
    {
        var incidents = Enumerable.Range(0, 3).Select(_ => Given("quota")).ToList();
        GivenRootCause("quota", incidents);
        incidents[2].Acknowledge("other-admin", Now.AddMinutes(-1));

        var result = await new AcknowledgeRootCauseCommandHandler(Triage())
            .Handle(new AcknowledgeRootCauseCommand("quota"), TestContext.Current.CancellationToken);

        result.Should().BeEquivalentTo(new BulkTransitionResultDto(3, 2, 1, []));
        incidents[2].AcknowledgedByUserId.Should().Be("other-admin");
    }

    [Fact]
    public async Task RootCause_WithNothingUnresolved_AttemptsNothing()
    {
        GivenRootCause("gone", []);

        var result = await new ResolveRootCauseCommandHandler(Triage())
            .Handle(new ResolveRootCauseCommand("gone", null), TestContext.Current.CancellationToken);

        result.Should().BeEquivalentTo(new BulkTransitionResultDto(0, 0, 0, []));
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void ResolveValidator_CapsTheNoteAt500Characters(int length, bool valid)
    {
        new ResolveIncidentCommandValidator().Validate(new ResolveIncidentCommand(Guid.NewGuid(), new string('n', length)))
            .IsValid.Should().Be(valid);
        new ResolveRootCauseCommandValidator().Validate(new ResolveRootCauseCommand("root-1", new string('n', length)))
            .IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task Summary_IsTheCountOfRootCausesWithAnUnacknowledgedCriticalIncident()
    {
        _store.CountUnacknowledgedCriticalRootCausesAsync(Arg.Any<CancellationToken>()).Returns(2);

        var summary = await new GetOperationalFailureSummaryQueryHandler(_store)
            .Handle(new GetOperationalFailureSummaryQuery(), TestContext.Current.CancellationToken);

        summary.UnacknowledgedCriticalRootCauses.Should().Be(2);
    }

    [Fact]
    public async Task ListRelated_OnAnUnknownIncident_IsNotFound()
    {
        _store.ListRelatedAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<OperationalFailureIncident>, int)?)null);
        var readModels = new OperationalFailureReadModelBuilder(_store, Substitute.For<IOperationalFailureReferenceLookup>());

        var act = () => new ListRelatedIncidentsQueryHandler(_store, readModels)
            .Handle(new ListRelatedIncidentsQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
