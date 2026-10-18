using AskLucy.Application.Abstractions;
using AskLucy.Infrastructure.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace AskLucy.Infrastructure.Tests.Ai;

public sealed class ProviderHealthCheckRetentionJobTests
{
    [Fact]
    public async Task Run_DeletesChecksOlderThanTheRetentionWindow_MeasuredFromNow()
    {
        var now = new DateTimeOffset(2026, 10, 15, 3, 30, 0, TimeSpan.Zero);
        var checks = Substitute.For<IProviderHealthCheckRepository>();
        checks.DeleteCheckedBeforeAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(7);
        var job = new ProviderHealthCheckRetentionJob(
            checks,
            Options.Create(new ProviderHealthCheckOptions { RetentionDays = 30 }),
            new FakeTimeProvider(now),
            NullLogger<ProviderHealthCheckRetentionJob>.Instance);

        await job.RunAsync(TestContext.Current.CancellationToken);

        await checks.Received(1).DeleteCheckedBeforeAsync(
            new DateTime(2026, 9, 15, 3, 30, 0, DateTimeKind.Utc), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Options_DefaultRetentionIsThirtyDays()
    {
        new ProviderHealthCheckOptions().RetentionDays.Should().Be(30);
    }
}
