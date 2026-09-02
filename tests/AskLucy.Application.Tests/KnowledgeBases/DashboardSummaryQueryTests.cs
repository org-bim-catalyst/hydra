using System.Globalization;
using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases;
using AskLucy.Application.KnowledgeBases.Queries.GetKnowledgeBaseDashboardSummary;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.KnowledgeBases;

/// <summary>Covers the cache-check-then-compute path (research.md Decision 7, FR-035) — a real <see cref="MemoryCache"/> is used (not mocked) because the behavior under test IS the caching, mirroring how the six command-handler tests exercise <see cref="KnowledgeBaseDashboardSummaryCache"/>.</summary>
public sealed class DashboardSummaryQueryTests
{
    private readonly IKnowledgeBaseRepository _repository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly KnowledgeBaseDashboardSummaryCache _cache = new(new MemoryCache(new MemoryCacheOptions()));
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-08-04T12:00:00Z", CultureInfo.InvariantCulture));

    [Fact]
    public async Task Handle_ShouldComputeAndCache_OnFirstCall()
    {
        _currentUser.UserId.Returns("user-1");
        var summary = new KnowledgeBaseDashboardSummaryDto(5, 20, 1024, 2, 1, 1, 0);
        _repository.GetDashboardSummaryAsync("user-1", _timeProvider.GetUtcNow().UtcDateTime.AddDays(-7), Arg.Any<CancellationToken>())
            .Returns(summary);
        var handler = new GetKnowledgeBaseDashboardSummaryQueryHandler(_repository, _cache, _timeProvider, _currentUser);

        var result = await handler.Handle(new GetKnowledgeBaseDashboardSummaryQuery(), CancellationToken.None);

        result.Should().Be(summary);
        await _repository.Received(1).GetDashboardSummaryAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnCachedValue_WithoutHittingTheRepository_OnSecondCall()
    {
        _currentUser.UserId.Returns("user-1");
        var summary = new KnowledgeBaseDashboardSummaryDto(5, 20, 1024, 2, 1, 1, 0);
        _repository.GetDashboardSummaryAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(summary);
        var handler = new GetKnowledgeBaseDashboardSummaryQueryHandler(_repository, _cache, _timeProvider, _currentUser);
        await handler.Handle(new GetKnowledgeBaseDashboardSummaryQuery(), CancellationToken.None);

        var result = await handler.Handle(new GetKnowledgeBaseDashboardSummaryQuery(), CancellationToken.None);

        result.Should().Be(summary);
        await _repository.Received(1).GetDashboardSummaryAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldRecompute_AfterInvalidation()
    {
        _currentUser.UserId.Returns("user-1");
        var first = new KnowledgeBaseDashboardSummaryDto(5, 20, 1024, 2, 1, 1, 0);
        var second = new KnowledgeBaseDashboardSummaryDto(6, 25, 2048, 3, 1, 1, 0);
        _repository.GetDashboardSummaryAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(first, second);
        var handler = new GetKnowledgeBaseDashboardSummaryQueryHandler(_repository, _cache, _timeProvider, _currentUser);
        await handler.Handle(new GetKnowledgeBaseDashboardSummaryQuery(), CancellationToken.None);
        _cache.Invalidate("user-1");

        var result = await handler.Handle(new GetKnowledgeBaseDashboardSummaryQuery(), CancellationToken.None);

        result.Should().Be(second);
        await _repository.Received(2).GetDashboardSummaryAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldNotLeakBetweenUsers_EachOwnerGetsItsOwnCacheEntry()
    {
        var userOneSummary = new KnowledgeBaseDashboardSummaryDto(5, 20, 1024, 2, 1, 1, 0);
        var userTwoSummary = new KnowledgeBaseDashboardSummaryDto(1, 2, 128, 0, 0, 0, 0);
        _repository.GetDashboardSummaryAsync("user-1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(userOneSummary);
        _repository.GetDashboardSummaryAsync("user-2", Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(userTwoSummary);
        var handler = new GetKnowledgeBaseDashboardSummaryQueryHandler(_repository, _cache, _timeProvider, _currentUser);

        _currentUser.UserId.Returns("user-1");
        var resultOne = await handler.Handle(new GetKnowledgeBaseDashboardSummaryQuery(), CancellationToken.None);
        _currentUser.UserId.Returns("user-2");
        var resultTwo = await handler.Handle(new GetKnowledgeBaseDashboardSummaryQuery(), CancellationToken.None);

        resultOne.Should().Be(userOneSummary);
        resultTwo.Should().Be(userTwoSummary);
    }

    [Fact]
    public async Task Handle_ShouldThrowUnauthorized_WhenNoCurrentUser()
    {
        _currentUser.UserId.Returns((string?)null);
        var handler = new GetKnowledgeBaseDashboardSummaryQueryHandler(_repository, _cache, _timeProvider, _currentUser);

        var act = () => handler.Handle(new GetKnowledgeBaseDashboardSummaryQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}

/// <summary>Minimal deterministic <see cref="TimeProvider"/> fake — no external test-time-provider package dependency needed for this one property (mirrors `AskLucy.Infrastructure.Tests.KnowledgeBases.FakeTimeProvider`, kept local rather than shared since that one is `internal` to its own assembly).</summary>
internal sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
