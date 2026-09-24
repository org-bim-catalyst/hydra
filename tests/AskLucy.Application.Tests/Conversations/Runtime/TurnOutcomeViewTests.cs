using System.Text.Json;
using AskLucy.Application.Conversations;
using AskLucy.Application.Conversations.Runtime;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/068 contracts/turn-outcome.md §1 — the redacted projection the client receives, and the
/// round trip between the persisted document and it.
/// </summary>
public sealed class TurnOutcomeViewTests
{
    private const string SecretArgument = "internal-place-id-9f3c";

    private static RecordedTurnOutcome AnActedOutcome(bool succeeded) => RecordedTurnOutcome.Acted(
        [succeeded
            ? ActionAttempt.Success("Capability", "resolve_location", "Al Safa Park 2", $$"""{"placeId":"{{SecretArgument}}"}""")
            : ActionAttempt.Failure("Capability", "resolve_location", "Al Safa Park 2", $$"""{"placeId":"{{SecretArgument}}"}""",
                "The AI provider rejected the request.")],
        DateTimeOffset.UtcNow);

    [Fact]
    public void From_ShouldKeepEverythingTheClientNeeds()
    {
        var view = TurnOutcomeView.From(AnActedOutcome(succeeded: true));

        view.Verdict.Should().Be(TurnVerdict.Acted);
        var attempt = view.Attempts.Should().ContainSingle().Subject;
        attempt.Kind.Should().Be("Capability");
        attempt.Key.Should().Be("resolve_location");
        attempt.TargetLabel.Should().Be("Al Safa Park 2");
        attempt.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void From_ShouldNotCarryTheRecordedArguments()
    {
        var serialized = JsonSerializer.Serialize(TurnOutcomeView.From(AnActedOutcome(succeeded: false)), RecordedTurnOutcomeJson.Options);

        // research.md D4 — retry is safe to accept as a bare message id precisely because these
        // never leave the server, so a client has nothing to send back.
        serialized.Should().NotContain(SecretArgument);
        serialized.Should().NotContainEquivalentOf("arguments");
    }

    [Fact]
    public void FromJson_ShouldRoundTripADocumentWrittenByTheSameOptions()
    {
        var persisted = JsonSerializer.Serialize(AnActedOutcome(succeeded: false), RecordedTurnOutcomeJson.Options);

        var view = TurnOutcomeView.FromJson(persisted);

        // SC-001c — a reloaded outcome must say exactly what the streamed one said.
        view.Should().NotBeNull();
        view!.Verdict.Should().Be(TurnVerdict.Acted);
        view.Attempts.Should().ContainSingle().Which.FailureReason.Should().Be("The AI provider rejected the request.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromJson_ShouldReportNoOutcome_WhenThereIsNoDocument(string? persisted)
    {
        // Every message written before this feature, and every user message. "Unknown", never
        // "succeeded".
        TurnOutcomeView.FromJson(persisted).Should().BeNull();
    }

    [Fact]
    public void FromJson_ShouldReportNoOutcome_AndLogIt_WhenTheDocumentIsUnreadable()
    {
        var logger = new CapturingLogger();
        var messageId = Guid.NewGuid();

        var view = TurnOutcomeView.FromJson("{ this is not json", messageId, logger);

        // Conservative, but not silent (constitution §2.VIII): one corrupt row must not take down
        // a whole transcript, and a document this code wrote and cannot read back is a real defect
        // an operator needs to see.
        view.Should().BeNull();
        logger.Entries.Should().ContainSingle()
            .Which.Should().Match<(LogLevel Level, string Message, Exception? Exception)>(
                e => e.Level == LogLevel.Error && e.Message.Contains(messageId.ToString()) && e.Exception is JsonException);
    }

    [Fact]
    public void FromJson_ShouldReportNoOutcome_WithoutLogging_WhenNoLoggerIsSupplied()
    {
        var act = () => TurnOutcomeView.FromJson("{ this is not json");

        act.Should().NotThrow().Which.Should().BeNull();
    }

    /// <summary>
    /// Hand-written rather than an NSubstitute double: <c>Received().Log(...)</c> never matches
    /// the <c>[LoggerMessage]</c> source generator's call shape, so the assertion would pass or
    /// fail for reasons unrelated to whether anything was logged.
    /// </summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
