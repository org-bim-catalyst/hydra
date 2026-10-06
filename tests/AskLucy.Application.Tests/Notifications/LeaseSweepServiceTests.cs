using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Processing;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Domain.Notifications;
using AskLucy.Domain.OperationalFailures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Notifications;

/// <summary>
/// <see cref="LeaseSweepService"/> (specs/067 research R4, R5, T118): the once-a-minute recovery pass sweeps
/// expired delivery leases into ambiguous failures and expired outbox leases back to pending, and puts a
/// finding on the operational failure trail only when sends were actually left in an unknown state.
/// </summary>
public sealed class LeaseSweepServiceTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.Zero));
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly INotificationOutboxStore _outbox = Substitute.For<INotificationOutboxStore>();
    private readonly IOperationalFailureRecorder _recorder = Substitute.For<IOperationalFailureRecorder>();
    private readonly ServiceProvider _services;

    public LeaseSweepServiceTests()
    {
        _services = new ServiceCollection()
            .AddSingleton(_notifications)
            .AddSingleton(_outbox)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public void Dispose() => _services.Dispose();

    private LeaseSweepService CreateSut() =>
        new(_services.GetRequiredService<IServiceScopeFactory>(), _recorder, _time, new FakeLogger<LeaseSweepService>());

    [Fact]
    public async Task Sweep_PassesTheCurrentTimeToBothStores_AndReportsWhatEachFound()
    {
        _notifications.SweepExpiredLeasesAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(3);
        _outbox.SweepExpiredLeasesAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(2);

        var result = await CreateSut().SweepAsync(CancellationToken.None);

        result.Should().Be(new LeaseSweepResult(AmbiguousDeliveries: 3, ReleasedEvents: 2));
        var now = _time.GetUtcNow().UtcDateTime;
        await _notifications.Received(1).SweepExpiredLeasesAsync(now, Arg.Any<CancellationToken>());
        await _outbox.Received(1).SweepExpiredLeasesAsync(now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sweep_WithAmbiguousDeliveries_PutsOneFindingOnTheOperationalFailureTrail()
    {
        _notifications.SweepExpiredLeasesAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(3);

        await CreateSut().SweepAsync(CancellationToken.None);

        _recorder.Received(1).Record(Arg.Is<OperationalFailureReport>(r =>
            r != null && r.Engine == OperationalFailureEngine.BackgroundJob && r.Reason.Contains('3', StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Sweep_WithNothingToDo_RecordsNothing()
    {
        var result = await CreateSut().SweepAsync(CancellationToken.None);

        result.Should().Be(new LeaseSweepResult(0, 0));
        _recorder.DidNotReceiveWithAnyArgs().Record(default!);
    }

    [Fact]
    public async Task Sweep_OutboxEventsReleasedAlone_AreNotAnOperationalFailure()
    {
        _outbox.SweepExpiredLeasesAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(4);

        await CreateSut().SweepAsync(CancellationToken.None);

        _recorder.DidNotReceiveWithAnyArgs().Record(default!);
    }

    [Fact]
    public async Task Sweep_AFailingStore_Propagates_SoTheHangfireJobIsRecordedAsFailed()
    {
        _notifications.SweepExpiredLeasesAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new InvalidOperationException("database unreachable"));

        var act = () => CreateSut().SweepAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
