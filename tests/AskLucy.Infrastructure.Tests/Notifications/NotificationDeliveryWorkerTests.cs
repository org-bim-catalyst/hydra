using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Processing;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using AskLucy.Infrastructure.Notifications;
using AskLucy.Infrastructure.Notifications.Workers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Notifications;

/// <summary>
/// <see cref="NotificationDeliveryWorker"/> (specs/067 research R3 to R5, T117): the same loop, scope,
/// heartbeat and error rules as the outbox dispatcher. A failing pass is logged and backed off rather than
/// ending the loop, the heartbeat shows the loop is alive, and a stop (even one right after a start, when the
/// loop may never have run) leaves nothing claimed.
/// </summary>
public sealed class NotificationDeliveryWorkerTests : IDisposable
{
    private readonly INotificationRepository _repository = Substitute.For<INotificationRepository>();
    private readonly NotificationWakeSignal _wake = new();
    private readonly NotificationWorkerHeartbeats _heartbeats = new();
    private readonly FakeLogger<NotificationDeliveryWorker> _logger = new();
    private readonly ServiceProvider _services;
    private readonly DeliveryProcessingService _processing;

    public NotificationDeliveryWorkerTests()
    {
        var channels = Substitute.For<INotificationChannelRegistry>();
        channels.AvailableChannels.Returns(new HashSet<NotificationChannel> { NotificationChannel.InApp, NotificationChannel.Email });
        _repository.ClaimDueDeliveriesAsync(default!, default!, default, default, default, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<Guid>>([]));

        _services = new ServiceCollection().AddSingleton(_repository).BuildServiceProvider();
        _processing = new DeliveryProcessingService(
            _services.GetRequiredService<IServiceScopeFactory>(), channels, Options.Create(new NotificationsOptions()), TimeProvider.System,
            new FakeLogger<DeliveryProcessingService>());
    }

    public void Dispose() => _services.Dispose();

    private NotificationDeliveryWorker CreateSut()
    {
        var monitor = Substitute.For<IOptionsMonitor<NotificationsOptions>>();
        monitor.CurrentValue.Returns(new NotificationsOptions { Dispatch = new NotificationDispatchOptions { IdlePollIntervalSeconds = 1, PollIntervalSeconds = 0 } });
        return new NotificationDeliveryWorker(_processing, _wake, _heartbeats, monitor, TimeProvider.System, _logger);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for: {because}");
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Worker_RunsAPass_AndRecordsItsHeartbeat()
    {
        var sut = CreateSut();

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => _heartbeats.DeliveryWorker is not null, "the first heartbeat");
        await sut.StopAsync(TestContext.Current.CancellationToken);

        await _repository.ReceivedWithAnyArgs().ClaimDueDeliveriesAsync(default!, default!, default, default, default, TestContext.Current.CancellationToken);
        _heartbeats.DeliveryWorker.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Worker_ClaimsOnlyChannelsThatHaveASender_NotInApp()
    {
        var sut = CreateSut();

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => _heartbeats.DeliveryWorker is not null, "the first heartbeat");
        await sut.StopAsync(TestContext.Current.CancellationToken);

        await _repository.Received().ClaimDueDeliveriesAsync(
            Arg.Any<string>(), Arg.Is<IReadOnlyCollection<NotificationChannel>>(c => c != null && c.Count == 1 && c.Contains(NotificationChannel.Email)),
            Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Worker_APassThatThrows_IsLogged_AndDoesNotEndTheLoop()
    {
        var calls = 0;
        _repository.ClaimDueDeliveriesAsync(default!, default!, default, default, default, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(_ =>
            {
                calls++;
                return calls == 1
                    ? Task.FromException<IReadOnlyList<Guid>>(new InvalidOperationException("database unreachable"))
                    : Task.FromResult<IReadOnlyList<Guid>>([]);
            });
        var sut = CreateSut();

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => calls >= 2, "a second pass after the failing one");
        await sut.StopAsync(TestContext.Current.CancellationToken);

        _logger.Collector.GetSnapshot().Should().Contain(r => r.Level == LogLevel.Error && r.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task Worker_PulsedByTheWakeSignal_DoesNotWaitForTheIdlePoll()
    {
        var sut = CreateSut();
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => _heartbeats.DeliveryWorker is not null, "the first pass");
        var callsBefore = _repository.ReceivedCalls().Count();

        _wake.PulseDeliveryWorker();

        await WaitUntilAsync(() => _repository.ReceivedCalls().Count() > callsBefore, "a pass right after the pulse");
        await sut.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Stop_RightAfterStart_IsPrompt_AndNeverThrows_EvenIfTheLoopNeverRan()
    {
        var sut = CreateSut();

        await sut.StartAsync(TestContext.Current.CancellationToken);
        var act = () => sut.StopAsync(TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Stop_OnAWorkerThatWasNeverStarted_ReleasesNothingAndDoesNotThrow()
    {
        var sut = CreateSut();

        var act = () => sut.StopAsync(TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
        await _repository.DidNotReceiveWithAnyArgs().ReleaseClaimsAsync(default!, default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void WorkerId_IsUniquePerInstance_SoARestartedHostNeverFinishesItsPredecessorsLeases()
    {
        CreateSut().WorkerId.Should().NotBe(CreateSut().WorkerId);
    }
}
