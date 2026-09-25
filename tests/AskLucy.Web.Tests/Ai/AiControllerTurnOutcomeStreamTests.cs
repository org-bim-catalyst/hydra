using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Chats;
using AskLucy.Application.Chats.Commands.AppendMessage;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.OperationalFailures;
using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using AskLucy.Web.Contracts;
using AskLucy.Web.Controllers.v1;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Web.Tests.Ai;

/// <summary>
/// specs/068 T024 (FR-004a, FR-004c, contracts/turn-outcome.md §1) — every turn puts exactly one
/// <c>__TURN_OUTCOME__</c> event on the wire, and that event never carries the recorded arguments.
///
/// <para>
/// Controller-level rather than orchestrator-level on purpose. Two of the three guarantees exist
/// only here: the mid-stream catch that turns a thrown turn into
/// <see cref="TurnVerdict.FailedBeforeCompleting"/> is the controller's, and so is the redaction
/// that strips <see cref="ActionAttempt.ArgumentsJson"/> on its way out. An Application-layer test
/// cannot see either — it would assert on the record the controller is about to redact.
/// </para>
///
/// <para>
/// Driven against the controller with a fake <see cref="ISender"/> and a
/// <see cref="DefaultHttpContext"/> writing into a <see cref="MemoryStream"/>, matching
/// <see cref="AiControllerChatStreamTests"/>: this is about bytes on the wire, not routing or auth.
/// </para>
/// </summary>
public sealed class AiControllerTurnOutcomeStreamTests : IDisposable
{
    private const string SecretArgument = "internal-place-id-9f3c";
    private const string RecordedArguments = $$"""{"placeId":"{{SecretArgument}}"}""";

    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly ISelectedActionResolver _selectedActionResolver = Substitute.For<ISelectedActionResolver>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly List<AppendMessageCommand> _appended = [];
    private readonly MemoryStream _responseBody = new();
    private readonly AiController _controller;
    private readonly Guid _chatId = Guid.NewGuid();

    public AiControllerTurnOutcomeStreamTests()
    {
        _currentUser.UserId.Returns("user-1");
        _mediator.Send(Arg.Any<AppendMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _appended.Add(call.Arg<AppendMessageCommand>()!);
                return new MessageDto(
                    Guid.NewGuid(), "assistant", "text", "Here you go.", null, DateTime.UtcNow,
                    null, null, null, null, null, null, null, null, null, [], []);
            });

        // A real TurnRecorder (it is sealed) over substituted repositories. Its orchestrator
        // lookup returns null, so every call is a logged no-op — which is the point: the advisory
        // audit trail must not affect what reaches the wire.
        var turnRecorder = new TurnRecorder(
            Substitute.For<IAgentRepository>(), Substitute.For<IAgentExecutionRepository>(),
            Substitute.For<IUnitOfWork>(), NullLogger<TurnRecorder>.Instance);

        _controller = new AiController(_mediator, _providers, _models, _selectedActionResolver,
            Substitute.For<IRetryTargetResolver>(),
            Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions()),
            turnRecorder, _currentUser,
            Substitute.For<AskLucy.Application.OperationalFailures.Abstractions.IOperationalFailureRecorder>(),
            new AskLucy.Application.OperationalFailures.FailureClassifier(),
            NullLogger<AiController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Response = { Body = _responseBody } },
            },
        };
    }

    public void Dispose() => _responseBody.Dispose();

    private string ResponseText() => Encoding.UTF8.GetString(_responseBody.ToArray());

    private static int CountOutcomeEvents(string responseText) =>
        responseText.Split("__TURN_OUTCOME__", StringSplitOptions.None).Length - 1;

    private Task RunTurnAsync(IAsyncEnumerable<ChatStreamChunk> stream)
    {
        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>()).Returns(stream);

        return _controller.Chat(
            new ChatRequest(_chatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], Guid.NewGuid(), Guid.NewGuid(), null),
            CancellationToken.None);
    }

    private static ActionAttempt ResolveLocationAttempt(bool succeeded) => succeeded
        ? ActionAttempt.Success("Capability", "resolve_location", "Al Safa Park 2", RecordedArguments)
        : ActionAttempt.Failure("Capability", "resolve_location", "Al Safa Park 2", RecordedArguments,
            "The AI provider rejected the request.");

    [Fact]
    public async Task Chat_ShouldEmitExactlyOneOutcomeEvent_ForATurnThatOnlyAnsweredInWords()
    {
        async IAsyncEnumerable<ChatStreamChunk> Stream()
        {
            yield return new ChatStreamChunk("Sure — I can search for you.", null);
            await Task.CompletedTask;
        }

        await RunTurnAsync(Stream());
        var text = ResponseText();

        // A turn that attempted nothing still reports, and reports it honestly. Silence here is
        // what FR-002c would have to read as "outcome unknown", costing the gate its evidence.
        CountOutcomeEvents(text).Should().Be(1);
        text.Should().Contain("\"verdict\":\"AnsweredInWords\"");
    }

    [Fact]
    public async Task Chat_ShouldEmitExactlyOneOutcomeEvent_ForATurnThatActed()
    {
        async IAsyncEnumerable<ChatStreamChunk> Stream()
        {
            yield return new ChatStreamChunk("Here it is.", null);
            yield return new ChatStreamChunk(null, null,
                TurnOutcome: RecordedTurnOutcome.Acted([ResolveLocationAttempt(succeeded: true)], DateTimeOffset.UtcNow));
            await Task.CompletedTask;
        }

        await RunTurnAsync(Stream());
        var text = ResponseText();

        CountOutcomeEvents(text).Should().Be(1);
        text.Should().Contain("\"verdict\":\"Acted\"")
            .And.Contain("\"key\":\"resolve_location\"")
            .And.Contain("\"targetLabel\":\"Al Safa Park 2\"")
            .And.Contain("\"succeeded\":true");
    }

    [Fact]
    public async Task Chat_ShouldEmitAFailedOutcome_BeforeTheFailureNotice_WhenTheTurnThrowsMidStream()
    {
        await RunTurnAsync(ThrowingStream(new HttpRequestException("the provider credential is invalid")));
        var text = ResponseText();

        // The defect this feature exists for: the turn died on a dead credential and left behind
        // nothing but prose, which the next turn read as a completed answer.
        CountOutcomeEvents(text).Should().Be(1);
        text.Should().Contain("\"verdict\":\"FailedBeforeCompleting\"");
        text.IndexOf("__TURN_OUTCOME__", StringComparison.Ordinal)
            .Should().BeLessThan(text.LastIndexOf(UserFacingFailureText.Retry, StringComparison.Ordinal),
                "the client must know the turn failed before it renders the sentence saying so");
    }

    [Fact]
    public async Task Chat_ShouldDescribeTheFailure_WithoutLeakingTheExceptionMessage()
    {
        await RunTurnAsync(ThrowingStream(new HttpRequestException("the provider credential is invalid")));
        var text = ResponseText();

        text.Should().Contain(UserFacingFailureText.Retry);
        text.Should().NotContain("credential is invalid", "an exception message is written for an operator, not a user");
    }

    [Fact]
    public async Task Chat_ShouldNeverPutTheRecordedArguments_OnTheWire()
    {
        async IAsyncEnumerable<ChatStreamChunk> Stream()
        {
            yield return new ChatStreamChunk("Here it is.", null);
            yield return new ChatStreamChunk(null, null,
                TurnOutcome: RecordedTurnOutcome.Acted([ResolveLocationAttempt(succeeded: true)], DateTimeOffset.UtcNow));
            await Task.CompletedTask;
        }

        await RunTurnAsync(Stream());
        var text = ResponseText();

        // research.md D4: retry is safe to accept as a bare message id precisely because the client
        // never sees these, and so has nothing to send back.
        text.Should().NotContainEquivalentOf("argumentsJson");
        text.Should().NotContain(SecretArgument);
    }

    [Fact]
    public async Task Chat_ShouldStillPersistTheRecordedArguments_ForReplay()
    {
        async IAsyncEnumerable<ChatStreamChunk> Stream()
        {
            yield return new ChatStreamChunk("It didn't work.", null);
            yield return new ChatStreamChunk(null, null,
                TurnOutcome: RecordedTurnOutcome.Acted([ResolveLocationAttempt(succeeded: false)], DateTimeOffset.UtcNow));
            await Task.CompletedTask;
        }

        await RunTurnAsync(Stream());

        // Redacted on the way out, kept on the way down: US2 replays the failed attempt from this.
        var persisted = _appended.Should().ContainSingle(c => c.TurnOutcomeJson != null).Subject;
        persisted.TurnOutcomeJson.Should().Contain(SecretArgument).And.Contain("\"succeeded\":false");
    }

    [Fact]
    public async Task Chat_ShouldPersistTheFailedOutcome_AlongsideTheFailureNotice()
    {
        await RunTurnAsync(ThrowingStream(new TimeoutException("upstream read timed out")));

        // FR-004c/FR-007 — the notice and the verdict are written together, so a reload cannot
        // present the notice as ordinary assistant prose the way the reported defect did.
        var persisted = _appended.Should().ContainSingle(c => c.TurnOutcomeJson != null).Subject;
        persisted.TurnOutcomeJson.Should().Contain("FailedBeforeCompleting");
        persisted.Content.Should().Contain(UserFacingFailureText.Retry);
    }

    /// <summary>
    /// A turn that streams some text and then dies — the shape of the reported defect, where the
    /// provider credential failed after the reply had already started.
    /// </summary>
    private static async IAsyncEnumerable<ChatStreamChunk> ThrowingStream(Exception failure)
    {
        yield return new ChatStreamChunk("Looking that up", null);
        await Task.Yield();
        throw failure;
    }
}
