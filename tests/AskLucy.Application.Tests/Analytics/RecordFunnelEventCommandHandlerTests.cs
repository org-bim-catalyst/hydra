using AskLucy.Application.Analytics.Commands.RecordFunnelEvent;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Analytics;

/// <summary>
/// specs/023-flumeria-landing-experience, research.md Topic 4 — funnel events are recorded
/// via structured Serilog logging, not a database table, so the only observable behavior is
/// the log call itself (no repository, no DbContext).
/// </summary>
public sealed class RecordFunnelEventCommandHandlerTests
{
    [Fact]
    public async Task Handle_ShouldLogInformation_ForACtaClickedEvent()
    {
        // [LoggerMessage]-generated methods guard with `if (logger.IsEnabled(...))`; an
        // unconfigured NSubstitute bool-returning member defaults to false, which would
        // silently skip the Log(...) call this test asserts on.
        var logger = Substitute.For<ILogger<RecordFunnelEventCommandHandler>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var handler = new RecordFunnelEventCommandHandler(logger);
        var command = new RecordFunnelEventCommand(
            FunnelEventType.CtaClicked, FunnelCtaId.SignUp, null, Guid.NewGuid(), DateTime.UtcNow);

        await handler.Handle(command, CancellationToken.None);

        // CA1873 false positive: this is an NSubstitute `Received()` call-verification, not a
        // live ILogger.Log invocation guarded by IsEnabled — the arguments are matchers
        // evaluated once during assertion, not "expensive logging arguments" on a hot path.
#pragma warning disable CA1873
        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o != null && o.ToString()!.Contains("CtaClicked", StringComparison.Ordinal)),
            null,
            Arg.Any<Func<object, Exception?, string>>());
#pragma warning restore CA1873
    }

    [Fact]
    public async Task Handle_ShouldLogInformation_ForAFunnelCompletedEvent()
    {
        // [LoggerMessage]-generated methods guard with `if (logger.IsEnabled(...))`; an
        // unconfigured NSubstitute bool-returning member defaults to false, which would
        // silently skip the Log(...) call this test asserts on.
        var logger = Substitute.For<ILogger<RecordFunnelEventCommandHandler>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var handler = new RecordFunnelEventCommandHandler(logger);
        var command = new RecordFunnelEventCommand(
            FunnelEventType.FunnelCompleted, null, FunnelKind.SignIn, Guid.NewGuid(), DateTime.UtcNow);

        await handler.Handle(command, CancellationToken.None);

        // CA1873 false positive: this is an NSubstitute `Received()` call-verification, not a
        // live ILogger.Log invocation guarded by IsEnabled — the arguments are matchers
        // evaluated once during assertion, not "expensive logging arguments" on a hot path.
#pragma warning disable CA1873
        logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o != null && o.ToString()!.Contains("FunnelCompleted", StringComparison.Ordinal)),
            null,
            Arg.Any<Func<object, Exception?, string>>());
#pragma warning restore CA1873
    }
}
