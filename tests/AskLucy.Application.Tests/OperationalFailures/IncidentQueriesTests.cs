using AskLucy.Application.Abstractions;
using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.OperationalFailures.Queries.GetIncident;
using AskLucy.Application.OperationalFailures.Queries.ListIncidents;
using AskLucy.Application.OperationalFailures.Queries.ListOccurrences;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.OperationalFailures;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;
using static AskLucy.Application.Tests.OperationalFailures.OperationalFailureTestData;

namespace AskLucy.Application.Tests.OperationalFailures;

/// <summary>specs/074 US1 — the list, detail and occurrence reads, and how every reference resolves.</summary>
public sealed class IncidentQueriesTests
{
    private readonly IOperationalFailureStore _store = Substitute.For<IOperationalFailureStore>();
    private readonly IOperationalFailureReferenceLookup _references = Substitute.For<IOperationalFailureReferenceLookup>();
    private readonly IAIProviderRepository _aiProviders = Substitute.For<IAIProviderRepository>();
    private readonly IEffectivePermissionResolver _permissions = Substitute.For<IEffectivePermissionResolver>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly FakeTimeProvider _time = new(Now);

    public IncidentQueriesTests()
    {
        _store.CountUnresolvedByRootCauseAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());
        _store.ListRecentUserIdsAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        _references.FindUsersAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, UserReference>());
        _references.FindItemsAsync(Arg.Any<ReferencedItemKind>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, ItemReference>());
        _permissions.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(PermissionSet.Empty);
        _currentUser.UserId.Returns("admin-1");
    }

    private OperationalFailureReadModelBuilder ReadModels() => new(_store, _references);

    private void GivenIncidents(params OperationalFailureIncident[] incidents) =>
        _store.ListIncidentsAsync(Arg.Any<IncidentFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((incidents, incidents.Length));

    private void GivenUsers(params UserReference[] users) =>
        _references.FindUsersAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(users.ToDictionary(u => u.Id, StringComparer.Ordinal));

    [Fact]
    public async Task ListIncidents_DefaultsToTheLastSevenDaysOfUnresolvedIncidents()
    {
        GivenIncidents();
        IncidentFilter? filter = null;
        await _store.ListIncidentsAsync(Arg.Do<IncidentFilter>(f => filter = f), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());

        var result = await new ListIncidentsQueryHandler(_store, ReadModels(), _time)
            .Handle(new ListIncidentsQuery(null, null, null, null, null, null, null, null), TestContext.Current.CancellationToken);

        result.Items.Should().BeEmpty();
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(25);
        filter.Should().NotBeNull();
        filter!.ToUtc.Should().Be(Now);
        filter.FromUtc.Should().Be(Now.AddDays(-7));
        filter.State.Should().Be(IncidentStateFilter.Unresolved);
    }

    [Fact]
    public async Task ListIncidents_PassesEveryFilterThrough()
    {
        GivenIncidents();
        IncidentFilter? filter = null;
        await _store.ListIncidentsAsync(Arg.Do<IncidentFilter>(f => filter = f), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        var from = Now.AddDays(-2);

        await new ListIncidentsQueryHandler(_store, ReadModels(), _time).Handle(
            new ListIncidentsQuery(
                from, Now, IncidentStateFilter.Resolved, OperationalFailureSeverity.Critical, OperationalFailureEngine.Voice,
                "ElevenLabs", OperationalFailureKind.QuotaExhausted, "user-7", 2, 10),
            TestContext.Current.CancellationToken);

        filter.Should().Be(new IncidentFilter(
            from, Now, IncidentStateFilter.Resolved, OperationalFailureSeverity.Critical, OperationalFailureEngine.Voice,
            "ElevenLabs", OperationalFailureKind.QuotaExhausted, "user-7"));
        await _store.Received(1).ListIncidentsAsync(Arg.Any<IncidentFilter>(), 2, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListIncidents_MapsTheSummaryAndCountsOtherOpenIncidentsSharingTheRootCause()
    {
        var incident = Incident(rootCauseKey: "openai-key", occurrenceCount: 14, distinctUserCount: 1);
        GivenIncidents(incident);
        _store.CountUnresolvedByRootCauseAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { ["openai-key"] = 3 });

        var result = await new ListIncidentsQueryHandler(_store, ReadModels(), _time)
            .Handle(new ListIncidentsQuery(null, null, null, null, null, null, null, null), TestContext.Current.CancellationToken);

        var summary = result.Items.Should().ContainSingle().Subject;
        summary.Id.Should().Be(incident.Id);
        summary.RowVersion.Should().Be(Convert.ToBase64String([1, 2, 3, 4]));
        summary.Severity.Should().Be(OperationalFailureSeverity.Critical);
        summary.OccurrenceCount.Should().Be(14);
        summary.DistinctUserCount.Should().Be(1);
        summary.State.Should().Be(IncidentTriageState.Open);
        summary.RelatedOpenCount.Should().Be(2, "the incident itself is one of the three unresolved");
        summary.Subject.Should().BeNull();
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task ListIncidents_ShowsADeletedSubjectAsDeletedAndAPurgedOneByItsRecordedLabel()
    {
        var deletedId = Guid.NewGuid();
        var purgedId = Guid.NewGuid();
        GivenIncidents(
            Incident(subjectType: "Workflow", subjectId: deletedId, subjectLabel: "Old name"),
            Incident(subjectType: "Workflow", subjectId: purgedId, subjectLabel: "Nightly import"));
        _references.FindItemsAsync(ReferencedItemKind.Workflow, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, ItemReference> { [deletedId] = new(deletedId, "Renamed", IsDeleted: true) });

        var result = await new ListIncidentsQueryHandler(_store, ReadModels(), _time)
            .Handle(new ListIncidentsQuery(null, null, null, null, null, null, null, null), TestContext.Current.CancellationToken);

        result.Items[0].Subject.Should().Be(new IncidentSubjectDto("Workflow", deletedId, "Renamed", Deleted: true));
        result.Items[1].Subject.Should().Be(new IncidentSubjectDto("Workflow", purgedId, "Nightly import", Deleted: true));
    }

    [Fact]
    public async Task ListOccurrences_ThrowsNotFoundForAnUnknownIncident()
    {
        var act = () => new ListOccurrencesQueryHandler(_store, ReadModels())
            .Handle(new ListOccurrencesQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ListOccurrences_ResolvesUsersAsActiveDeletedOrErased()
    {
        var incident = Incident();
        _store.GetIncidentAsync(incident.Id, Arg.Any<CancellationToken>()).Returns(incident);
        var active = Occurrence(incident.Id, new OperationalFailureReferences { UserId = "u-active" });
        var deleted = Occurrence(incident.Id, new OperationalFailureReferences { UserId = "u-deleted" });
        var gone = Occurrence(incident.Id, new OperationalFailureReferences { UserId = "u-gone" });
        var erased = Occurrence(incident.Id, new OperationalFailureReferences(), userErased: true);
        _store.ListOccurrencesAsync(incident.Id, 1, 50, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<OperationalFailureOccurrence>)[active, deleted, gone, erased], 4));
        GivenUsers(
            new UserReference("u-active", "Ada Lovelace", "ada@example.com", IsDeleted: false),
            new UserReference("u-deleted", "Old User", "old@example.com", IsDeleted: true));

        var result = await new ListOccurrencesQueryHandler(_store, ReadModels())
            .Handle(new ListOccurrencesQuery(incident.Id), TestContext.Current.CancellationToken);

        result.Items.Select(o => o.User).Should().Equal(
            new UserRefDto("u-active", "Ada Lovelace", "ada@example.com", UserRefStatus.Active),
            new UserRefDto("u-deleted", "Old User", "old@example.com", UserRefStatus.Deleted),
            UserRefDto.Erased,
            UserRefDto.Erased);
        result.TotalCount.Should().Be(4);
    }

    [Fact]
    public async Task ListOccurrences_LinksItemsAndShowsTheSourceAddressOnlyForAccessFailures()
    {
        var incident = Incident();
        var chatId = Guid.NewGuid();
        var workflowId = Guid.NewGuid();
        _store.GetIncidentAsync(incident.Id, Arg.Any<CancellationToken>()).Returns(incident);
        var chat = Occurrence(incident.Id, new OperationalFailureReferences { ChatId = chatId }, sourceIp: "10.0.0.1");
        var workflow = Occurrence(incident.Id, new OperationalFailureReferences { WorkflowId = workflowId });
        var access = Occurrence(incident.Id, new OperationalFailureReferences(), OperationalFailureEngine.Access, sourceIp: "10.0.0.2");
        _store.ListOccurrencesAsync(incident.Id, 1, 50, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<OperationalFailureOccurrence>)[chat, workflow, access], 3));
        _references.FindItemsAsync(ReferencedItemKind.Chat, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, ItemReference> { [chatId] = new(chatId, "Budget review", IsDeleted: false) });

        var result = await new ListOccurrencesQueryHandler(_store, ReadModels())
            .Handle(new ListOccurrencesQuery(incident.Id), TestContext.Current.CancellationToken);

        result.Items[0].Chat.Should().Be(new OccurrenceChatDto(chatId, "Budget review", Deleted: false));
        result.Items[0].SourceIp.Should().BeNull();
        result.Items[1].Workflow.Should().Be(new OccurrenceWorkflowDto(workflowId, null, null, null, Deleted: true));
        result.Items[2].SourceIp.Should().Be("10.0.0.2");
    }

    private GetIncidentQueryHandler GetIncidentHandler() => new(_store, ReadModels(), _aiProviders, _permissions, _currentUser);

    [Fact]
    public async Task GetIncident_ThrowsNotFoundForAnUnknownIncident()
    {
        var act = () => GetIncidentHandler().Handle(new GetIncidentQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetIncident_AddsProviderHealthCorrectiveActionTriageAndSampleUsers()
    {
        var provider = AIProvider.Create("openai", "OpenAI", "system");
        provider.Id = Guid.NewGuid();
        provider.UpdateHealthStatus(false, Now.AddMinutes(-5), AiProviderFailureKind.CredentialRejected, "Rejected");
        var incident = Incident(providerId: provider.Id);
        incident.Acknowledge("admin-2", Now.AddMinutes(-1));
        _store.GetIncidentAsync(incident.Id, Arg.Any<CancellationToken>()).Returns(incident);
        _aiProviders.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);
        _store.ListRecentUserIdsAsync(incident.Id, 10, Arg.Any<CancellationToken>()).Returns(["u-1", "u-erased"]);
        GivenUsers(
            new UserReference("u-1", "Ada Lovelace", "ada@example.com", IsDeleted: false),
            new UserReference("admin-2", "Grace Hopper", "grace@example.com", IsDeleted: false));

        var detail = await GetIncidentHandler().Handle(new GetIncidentQuery(incident.Id), TestContext.Current.CancellationToken);

        detail.Id.Should().Be(incident.Id);
        detail.ProviderHealth.Should().Be(new ProviderHealthDto(
            ProviderHealthStatus.Unhealthy, AiProviderFailureKind.CredentialRejected, Now.AddMinutes(-5)));
        detail.CorrectiveAction.AdminRoute.Should().Be($"/admin/ai-providers?select={provider.Id}");
        detail.Acknowledged.Should().Be(new IncidentAcknowledgementDto(
            new UserRefDto("admin-2", "Grace Hopper", "grace@example.com", UserRefStatus.Active), Now.AddMinutes(-1)));
        detail.Resolved.Should().BeNull();
        detail.SampleUsers.Select(u => u.Status).Should().Equal(UserRefStatus.Active, UserRefStatus.Erased);
    }

    [Fact]
    public async Task GetIncident_HasNoProviderHealthWhenTheProviderIsNotAnAiProvider()
    {
        var incident = Incident(OperationalFailureEngine.Voice, providerId: Guid.NewGuid(), providerName: "ElevenLabs");
        _store.GetIncidentAsync(incident.Id, Arg.Any<CancellationToken>()).Returns(incident);

        var detail = await GetIncidentHandler().Handle(new GetIncidentQuery(incident.Id), TestContext.Current.CancellationToken);

        detail.ProviderHealth.Should().BeNull();
        detail.CorrectiveAction.AdminRoute.Should().StartWith("/admin/voice");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GetIncident_ReportsWhatTheViewerMayDo(bool canManage, bool canViewContent)
    {
        var incident = Incident();
        _store.GetIncidentAsync(incident.Id, Arg.Any<CancellationToken>()).Returns(incident);
        var keys = new List<string> { AdminPermissionCatalog.OperationalFailuresView };
        if (canManage)
        {
            keys.Add(AdminPermissionCatalog.OperationalFailuresManage);
        }

        if (canViewContent)
        {
            keys.Add(AdminPermissionCatalog.OperationalFailuresContentView);
        }

        _permissions.ResolveAsync("admin-1", Arg.Any<CancellationToken>()).Returns(PermissionSet.Create(keys));

        var detail = await GetIncidentHandler().Handle(new GetIncidentQuery(incident.Id), TestContext.Current.CancellationToken);

        detail.CanManage.Should().Be(canManage);
        detail.CanViewContent.Should().Be(canViewContent);
    }
}
