using AskLucy.Application.Abstractions;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Infrastructure.KnowledgeBases;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.KnowledgeBases;

/// <summary>Sweep logic tested via an injected <see cref="TimeProvider"/> (fake, deterministic) — no real 30-day wait, mirrors the intent of <c>ProviderHealthCheckHostedService</c>'s cycle-failure isolation.</summary>
public sealed class KnowledgeBasePurgeHostedServiceTests
{
    private readonly IKnowledgeBaseRepository _repository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IKnowledgeBaseDocumentRepository _documentRepository = Substitute.For<IKnowledgeBaseDocumentRepository>();
    private readonly IKnowledgeBaseAuditLogRepository _auditLogRepository = Substitute.For<IKnowledgeBaseAuditLogRepository>();
    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private KnowledgeBasePurgeHostedService CreateService(TimeProvider timeProvider)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_repository);
        services.AddSingleton(_documentRepository);
        services.AddSingleton(_auditLogRepository);
        services.AddSingleton(_fileStorage);
        services.AddSingleton(_unitOfWork);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new KnowledgeBasePurgeHostedService(
            scopeFactory, timeProvider, Options.Create(new KnowledgeBasePurgeOptions()), NullLogger<KnowledgeBasePurgeHostedService>.Instance);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldPurgeKnowledgeBasesPastTheirSchedule()
    {
        var now = DateTimeOffset.UtcNow;
        var timeProvider = new FakeTimeProvider(now);
        var dueKnowledgeBase = KnowledgeBase.Create("Overdue", "user-1", "user-1");
        dueKnowledgeBase.SoftDelete("user-1");
        _repository.ListPastPurgeScheduleAsync(now.UtcDateTime, Arg.Any<CancellationToken>()).Returns([dueKnowledgeBase]);
        _documentRepository.ListByKnowledgeBaseIdIncludingDeletedAsync(dueKnowledgeBase.Id, Arg.Any<CancellationToken>())
            .Returns([]);

        await CreateService(timeProvider).RunOnceAsync(CancellationToken.None);

        _auditLogRepository.Received(1).Add(Arg.Is<KnowledgeBaseAuditLog>(
            a => a != null && a.KnowledgeBaseId == dueKnowledgeBase.Id && a.Action == KnowledgeBaseAuditAction.PermanentlyDeleted));
        await _repository.Received(1).PurgeAsync(dueKnowledgeBase.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnceAsync_ShouldNotPurgeKnowledgeBasesNotYetDue()
    {
        var now = DateTimeOffset.UtcNow;
        var timeProvider = new FakeTimeProvider(now);
        _repository.ListPastPurgeScheduleAsync(now.UtcDateTime, Arg.Any<CancellationToken>()).Returns([]);

        await CreateService(timeProvider).RunOnceAsync(CancellationToken.None);

        await _repository.DidNotReceive().PurgeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnceAsync_ShouldContinueSweepingOthers_WhenOneKnowledgeBaseFailsToPurge()
    {
        var now = DateTimeOffset.UtcNow;
        var timeProvider = new FakeTimeProvider(now);
        var failing = KnowledgeBase.Create("Failing", "user-1", "user-1");
        failing.SoftDelete("user-1");
        var succeeding = KnowledgeBase.Create("Succeeding", "user-1", "user-1");
        succeeding.SoftDelete("user-1");
        _repository.ListPastPurgeScheduleAsync(now.UtcDateTime, Arg.Any<CancellationToken>()).Returns([failing, succeeding]);
        _documentRepository.ListByKnowledgeBaseIdIncludingDeletedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _repository.PurgeAsync(failing.Id, Arg.Any<CancellationToken>()).Returns<Task>(_ => throw new InvalidOperationException("simulated failure"));

        var act = () => CreateService(timeProvider).RunOnceAsync(CancellationToken.None);

        await act.Should().NotThrowAsync("one knowledge base's purge failure must not stop the sweep or crash the host");
        await _repository.Received(1).PurgeAsync(succeeding.Id, Arg.Any<CancellationToken>());
    }
}

/// <summary>Minimal deterministic <see cref="TimeProvider"/> fake — no external test-time-provider package dependency needed for this one property.</summary>
internal sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
