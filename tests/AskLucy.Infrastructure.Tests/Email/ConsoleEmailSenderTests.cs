using System.Linq;
using AskLucy.Infrastructure.Email;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Email;

/// <summary>
/// specs/061-branded-email-templates T022 — confirms the dev sender logs both the HTML and
/// plain-text bodies, since the two must never silently diverge. [LoggerMessage] source-gen
/// logging needs IsEnabled stubbed and ReceivedCalls() inspected — a plain Received().Log(...)
/// assertion never matches the generated TState struct.
/// </summary>
public sealed class ConsoleEmailSenderTests
{
    private readonly ILogger<ConsoleEmailSender> _logger = Substitute.For<ILogger<ConsoleEmailSender>>();
    private readonly ConsoleEmailSender _sender;

    public ConsoleEmailSenderTests()
    {
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        _sender = new ConsoleEmailSender(_logger);
    }

    [Fact]
    public async Task SendAsync_ShouldLogBothTheHtmlAndTextBodies()
    {
        await _sender.SendAsync(
            "user@example.com", "Confirm your Ask Lucy account", "<p>html body</p>", "text body", TestContext.Current.CancellationToken);

        var logged = _logger.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(c => c.GetArguments()[2]?.ToString() ?? string.Empty)
            .ToList();

        logged.Should().ContainSingle(message => message.Contains("<p>html body</p>") && message.Contains("text body"));
    }
}
