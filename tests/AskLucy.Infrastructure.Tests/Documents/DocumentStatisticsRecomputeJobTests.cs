using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;
using AskLucy.Infrastructure.Documents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AskLucy.Infrastructure.Tests.Documents;

/// <summary>T115 — <c>DocumentStatisticsRecomputeJob</c> refreshes both per-user and organization-wide <see cref="DocumentStatistics"/> rows from fixture aggregates.</summary>
public sealed class DocumentStatisticsRecomputeJobTests
{
    private readonly IDocumentStatisticsRepository _statisticsRepository = Substitute.For<IDocumentStatisticsRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private DocumentStatisticsRecomputeJob CreateSut() =>
        new(_statisticsRepository, _unitOfWork, NullLogger<DocumentStatisticsRecomputeJob>.Instance);

    [Fact]
    public async Task RecomputeAllAsync_ShouldRefreshTheOrganizationRow_UsingTheOrganizationWideAggregate()
    {
        _statisticsRepository.ListDistinctOwnerIdsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)[]);
        _statisticsRepository.ComputeAggregateAsync(null, Arg.Any<CancellationToken>())
            .Returns(new DocumentStatisticsAggregate(128, 943718400, 41230, "{\"Pdf\":60}", "{\"en\":100}"));
        _statisticsRepository.GetByScopeAsync(DocumentStatisticsScope.Organization, null, Arg.Any<CancellationToken>())
            .Returns((DocumentStatistics?)null);

        DocumentStatistics? added = null;
        _statisticsRepository.When(r => r.Add(Arg.Any<DocumentStatistics>())).Do(c => added = c.Arg<DocumentStatistics>());

        await CreateSut().RecomputeAllAsync(CancellationToken.None);

        added.Should().NotBeNull();
        added!.Scope.Should().Be(DocumentStatisticsScope.Organization);
        added.TotalDocuments.Should().Be(128);
        added.TotalStorageBytes.Should().Be(943718400);
        added.FileTypeDistributionJson.Should().Be("{\"Pdf\":60}");
    }

    [Fact]
    public async Task RecomputeAllAsync_ShouldRefreshAnExistingRow_InsteadOfAddingASecondOne()
    {
        var existing = DocumentStatistics.CreateForOrganization("system");
        _statisticsRepository.ListDistinctOwnerIdsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)[]);
        _statisticsRepository.ComputeAggregateAsync(null, Arg.Any<CancellationToken>())
            .Returns(new DocumentStatisticsAggregate(50, 1000, null, "{}", "{}"));
        _statisticsRepository.GetByScopeAsync(DocumentStatisticsScope.Organization, null, Arg.Any<CancellationToken>()).Returns(existing);

        await CreateSut().RecomputeAllAsync(CancellationToken.None);

        existing.TotalDocuments.Should().Be(50);
        _statisticsRepository.DidNotReceive().Add(Arg.Any<DocumentStatistics>());
    }

    [Fact]
    public async Task RecomputeAllAsync_ShouldRefreshEveryDistinctOwnersRow()
    {
        _statisticsRepository.ListDistinctOwnerIdsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)["user-1", "user-2"]);
        _statisticsRepository.ComputeAggregateAsync(null, Arg.Any<CancellationToken>())
            .Returns(new DocumentStatisticsAggregate(0, 0, null, "{}", "{}"));
        _statisticsRepository.ComputeAggregateAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new DocumentStatisticsAggregate(5, 500, null, "{}", "{}"));
        _statisticsRepository.ComputeAggregateAsync("user-2", Arg.Any<CancellationToken>())
            .Returns(new DocumentStatisticsAggregate(9, 900, null, "{}", "{}"));
        _statisticsRepository.GetByScopeAsync(Arg.Any<DocumentStatisticsScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((DocumentStatistics?)null);

        var added = new List<DocumentStatistics>();
        _statisticsRepository.When(r => r.Add(Arg.Any<DocumentStatistics>())).Do(c => added.Add(c.Arg<DocumentStatistics>()!));

        await CreateSut().RecomputeAllAsync(CancellationToken.None);

        added.Should().Contain(s => s.OwnerId == "user-1" && s.TotalDocuments == 5);
        added.Should().Contain(s => s.OwnerId == "user-2" && s.TotalDocuments == 9);
    }

    [Fact]
    public async Task RecomputeAllAsync_ShouldContinueToTheNextOwner_WhenOneOwnersRecomputeThrows()
    {
        _statisticsRepository.ListDistinctOwnerIdsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)["broken-owner", "user-2"]);
        _statisticsRepository.ComputeAggregateAsync(null, Arg.Any<CancellationToken>())
            .Returns(new DocumentStatisticsAggregate(0, 0, null, "{}", "{}"));
        _statisticsRepository.ComputeAggregateAsync("broken-owner", Arg.Any<CancellationToken>())
            .Returns<DocumentStatisticsAggregate>(_ => throw new InvalidOperationException("Simulated transient failure."));
        _statisticsRepository.ComputeAggregateAsync("user-2", Arg.Any<CancellationToken>())
            .Returns(new DocumentStatisticsAggregate(3, 300, null, "{}", "{}"));
        _statisticsRepository.GetByScopeAsync(Arg.Any<DocumentStatisticsScope>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((DocumentStatistics?)null);

        var added = new List<DocumentStatistics>();
        _statisticsRepository.When(r => r.Add(Arg.Any<DocumentStatistics>())).Do(c => added.Add(c.Arg<DocumentStatistics>()!));

        await CreateSut().RecomputeAllAsync(CancellationToken.None);

        added.Should().Contain(s => s.OwnerId == "user-2" && s.TotalDocuments == 3);
    }
}
