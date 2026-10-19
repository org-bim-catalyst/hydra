using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Domain.Ai;
using AskLucy.Infrastructure.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace AskLucy.Infrastructure.Tests.Ai;

public sealed class ProviderHealthCheckRetentionJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 3, 30, 0, TimeSpan.Zero);

    private readonly IProviderHealthCheckRepository _checks = Substitute.For<IProviderHealthCheckRepository>();
    private readonly IProviderHealthRetentionRepository _retention = Substitute.For<IProviderHealthRetentionRepository>();

    public ProviderHealthCheckRetentionJobTests()
    {
        _checks.DeleteCheckedBeforeAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(7);
    }

    private ProviderHealthCheckRetentionJob Job(int fallbackDays = 30) => new(
        _checks,
        _retention,
        Options.Create(new ProviderHealthCheckOptions { RetentionDays = fallbackDays }),
        new FakeTimeProvider(Now),
        NullLogger<ProviderHealthCheckRetentionJob>.Instance);

    [Fact]
    public async Task Run_UsesTheStoredWindow_WhenTheSingletonRowExists()
    {
        _retention.GetAsync(Arg.Any<CancellationToken>())
            .Returns(ProviderHealthRetentionSetting.Create(7, "system", Now.UtcDateTime));

        await Job().RunAsync(TestContext.Current.CancellationToken);

        await _checks.Received(1).DeleteCheckedBeforeAsync(
            new DateTime(2026, 10, 8, 3, 30, 0, DateTimeKind.Utc), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_FallsBackToConfiguration_WhenTheRowIsMissing()
    {
        _retention.GetAsync(Arg.Any<CancellationToken>()).Returns((ProviderHealthRetentionSetting?)null);

        await Job(fallbackDays: 30).RunAsync(TestContext.Current.CancellationToken);

        await _checks.Received(1).DeleteCheckedBeforeAsync(
            new DateTime(2026, 9, 15, 3, 30, 0, DateTimeKind.Utc), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Options_DefaultRetentionIsThirtyDays()
    {
        new ProviderHealthCheckOptions().RetentionDays.Should().Be(30);
    }
}
