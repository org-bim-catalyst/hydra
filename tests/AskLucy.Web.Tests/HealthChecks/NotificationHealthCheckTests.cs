using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Infrastructure.Email;
using AskLucy.Infrastructure.Notifications;
using AskLucy.Infrastructure.Notifications.HealthChecks;
using AskLucy.Infrastructure.Notifications.Workers;
using AskLucy.Web.HealthChecks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace AskLucy.Web.Tests.HealthChecks;

/// <summary>
/// T156 (specs/067 US6, FR-057, research R21) — the four <c>notifications-*</c> readiness checks. Unit-level, with a fake clock: the
/// thresholds are the contract, and they are cheaper to prove here than by waiting out real minutes.
/// </summary>
public sealed class NotificationHealthCheckTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly NotificationWorkerHeartbeats _heartbeats;
    private readonly IOptionsMonitor<NotificationsOptions> _options = Substitute.For<IOptionsMonitor<NotificationsOptions>>();

    public NotificationHealthCheckTests()
    {
        _heartbeats = new NotificationWorkerHeartbeats(_time);
        _options.CurrentValue.Returns(new NotificationsOptions());
    }

    private static Task<HealthCheckResult> RunAsync(IHealthCheck check) => check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

    // ---- workers: the heartbeat ----

    [Fact]
    public async Task ADispatcherThatBeatLessThanThirtySecondsAgo_IsHealthy()
    {
        _heartbeats.RecordDispatcher(_time.GetUtcNow());
        _time.Advance(TimeSpan.FromSeconds(29));

        (await RunAsync(new NotificationDispatcherHealthCheck(_heartbeats, _options, _time))).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task ADispatcherThatBeatExactlyThirtySecondsAgo_IsStillHealthy()
    {
        _heartbeats.RecordDispatcher(_time.GetUtcNow());
        _time.Advance(TimeSpan.FromSeconds(30));

        (await RunAsync(new NotificationDispatcherHealthCheck(_heartbeats, _options, _time))).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task ADispatcherWhoseHeartbeatIsOlderThanThirtySeconds_IsUnhealthy()
    {
        _heartbeats.RecordDispatcher(_time.GetUtcNow());
        _time.Advance(TimeSpan.FromSeconds(31));

        var result = await RunAsync(new NotificationDispatcherHealthCheck(_heartbeats, _options, _time));

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("dispatcher");
    }

    [Fact]
    public async Task ADeliveryWorkerWhoseHeartbeatIsOlderThanThirtySeconds_IsUnhealthy()
    {
        _heartbeats.RecordDeliveryWorker(_time.GetUtcNow());
        _time.Advance(TimeSpan.FromSeconds(31));

        (await RunAsync(new NotificationDeliveryWorkerHealthCheck(_heartbeats, _options, _time))).Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task EachWorkerIsJudgedByItsOwnHeartbeat()
    {
        _heartbeats.RecordDeliveryWorker(_time.GetUtcNow());
        _time.Advance(TimeSpan.FromSeconds(60));
        _heartbeats.RecordDeliveryWorker(_time.GetUtcNow());

        (await RunAsync(new NotificationDeliveryWorkerHealthCheck(_heartbeats, _options, _time))).Status.Should().Be(HealthStatus.Healthy);
        (await RunAsync(new NotificationDispatcherHealthCheck(_heartbeats, _options, _time))).Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task AWorkerThatHasNotBeatYet_GetsTheSameWindowFromProcessStart_ThenIsUnhealthy()
    {
        var check = new NotificationDispatcherHealthCheck(_heartbeats, _options, _time);

        _time.Advance(TimeSpan.FromSeconds(10));
        (await RunAsync(check)).Status.Should().Be(HealthStatus.Healthy, "a deployment must not flap while the worker starts");

        _time.Advance(TimeSpan.FromSeconds(30));
        (await RunAsync(check)).Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task TheStaleWindow_IsConfigurable()
    {
        _options.CurrentValue.Returns(new NotificationsOptions { HealthChecks = new NotificationHealthCheckOptions { HeartbeatStaleSeconds = 120 } });
        _heartbeats.RecordDispatcher(_time.GetUtcNow());
        _time.Advance(TimeSpan.FromSeconds(100));

        (await RunAsync(new NotificationDispatcherHealthCheck(_heartbeats, _options, _time))).Status.Should().Be(HealthStatus.Healthy);
    }

    // ---- backlog ----

    private NotificationBacklogHealthCheck BacklogCheck(DeliveryBacklog outbox, DeliveryBacklog deliveries)
    {
        var outboxStore = Substitute.For<INotificationOutboxStore>();
        outboxStore.GetDueBacklogAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(outbox);
        var repository = Substitute.For<INotificationRepository>();
        repository.GetDueBacklogAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(deliveries);
        var services = new ServiceCollection().AddSingleton(outboxStore).AddSingleton(repository).BuildServiceProvider();
        return new NotificationBacklogHealthCheck(services.GetRequiredService<IServiceScopeFactory>(), new NotificationMetrics(new TestMeterFactory()), _options, _time);
    }

    private static DeliveryBacklog Due(TimeSpan age, TimeProvider time) => new(1, time.GetUtcNow().UtcDateTime - age);

    private static readonly DeliveryBacklog Empty = new(0, null);

    [Fact]
    public async Task NothingWaiting_IsHealthy() =>
        (await RunAsync(BacklogCheck(Empty, Empty))).Status.Should().Be(HealthStatus.Healthy);

    [Fact]
    public async Task AnItemWaitingUnderFiveMinutes_IsHealthy() =>
        (await RunAsync(BacklogCheck(Empty, Due(TimeSpan.FromMinutes(4), _time)))).Status.Should().Be(HealthStatus.Healthy);

    [Fact]
    public async Task AnItemWaitingOverFiveMinutes_IsDegraded() =>
        (await RunAsync(BacklogCheck(Empty, Due(TimeSpan.FromMinutes(6), _time)))).Status.Should().Be(HealthStatus.Degraded);

    [Fact]
    public async Task AnItemWaitingOverThirtyMinutes_IsUnhealthy() =>
        (await RunAsync(BacklogCheck(Empty, Due(TimeSpan.FromMinutes(31), _time)))).Status.Should().Be(HealthStatus.Unhealthy);

    [Fact]
    public async Task TheOldestOfTheOutboxAndTheDeliveriesDecides()
    {
        var result = await RunAsync(BacklogCheck(Due(TimeSpan.FromMinutes(40), _time), Due(TimeSpan.FromMinutes(1), _time)));

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Data["outboxPending"].Should().Be(1);
        result.Data["deliveriesDue"].Should().Be(1);
    }

    // ---- mail host ----

    private static IOptions<SmtpOptions> Smtp(string host = "mail.example.com") =>
        Microsoft.Extensions.Options.Options.Create(new SmtpOptions { Host = host, Username = "user", Password = "super-secret-password" });

    [Fact]
    public async Task AMailHostThatAnswers_IsHealthy_AndTheSummaryIsSafe()
    {
        var probe = Substitute.For<ISmtpProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns("STARTTLS ok");

        var result = await RunAsync(new NotificationSmtpHealthCheck(probe, Smtp(), _options, _time));

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("STARTTLS ok");
    }

    [Fact]
    public async Task AProbeFailure_IsOnlyEverDegraded_NeverUnhealthy()
    {
        var probe = Substitute.For<ISmtpProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns<string>(_ => throw new InvalidOperationException("535 auth failed for user:super-secret-password banner mail.example.com ESMTP"));

        var result = await RunAsync(new NotificationSmtpHealthCheck(probe, Smtp(), _options, _time));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().NotContain("super-secret-password").And.NotContain("banner").And.NotContain("mail.example.com");
        result.Exception.Should().BeNull("an exception can carry the server's banner or an account name");
    }

    [Fact]
    public async Task ANotConfiguredMailHost_IsDegraded_WithoutProbing()
    {
        var probe = Substitute.For<ISmtpProbe>();

        var result = await RunAsync(new NotificationSmtpHealthCheck(probe, Smtp(host: ""), _options, _time));

        result.Status.Should().Be(HealthStatus.Degraded);
        await probe.DidNotReceiveWithAnyArgs().ProbeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheProbeResult_IsCachedForFiveMinutes_SoReadinessPollingNeverHammersTheHost()
    {
        var probe = Substitute.For<ISmtpProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns("STARTTLS ok");
        var check = new NotificationSmtpHealthCheck(probe, Smtp(), _options, _time);

        await RunAsync(check);
        _time.Advance(TimeSpan.FromMinutes(4));
        await RunAsync(check);
        await RunAsync(check);
        await probe.Received(1).ProbeAsync(Arg.Any<CancellationToken>());

        _time.Advance(TimeSpan.FromMinutes(2));
        await RunAsync(check);
        await probe.Received(2).ProbeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedProbe_IsCachedToo_SoAnOutageDoesNotMeanAConnectionPerPoll()
    {
        var probe = Substitute.For<ISmtpProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns<string>(_ => throw new TimeoutException());
        var check = new NotificationSmtpHealthCheck(probe, Smtp(), _options, _time);

        await RunAsync(check);
        await RunAsync(check);
        await RunAsync(check);

        await probe.Received(1).ProbeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShuttingDownWhileProbing_IsNotReportedAsAMailOutage()
    {
        var probe = Substitute.For<ISmtpProbe>();
        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns<string>(_ => throw new OperationCanceledException());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => new NotificationSmtpHealthCheck(probe, Smtp(), _options, _time).CheckHealthAsync(new HealthCheckContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class TestMeterFactory : System.Diagnostics.Metrics.IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) => new(options);

        public void Dispose()
        {
        }
    }
}
