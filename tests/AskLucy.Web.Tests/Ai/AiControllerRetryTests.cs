using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Chats;
using AskLucy.Application.Chats.Commands.AppendMessage;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Common;
using AskLucy.Domain.Conversations;
using AskLucy.Web.Contracts;
using AskLucy.Web.Controllers.v1;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AskLucy.Web.Tests.Ai;

/// <summary>
/// specs/068 T053 — the transcript a retry leaves behind, and the boundary around what a client
/// may ask for.
///
/// <para>
/// The shape SC-012 fixes is: <b>two assistant turns, each with its own retrievable outcome, and no
/// user message nobody typed</b>. That last part is the deliberate divergence from selected-action
/// dispatch (FR-013b) — a selection has the user's own words behind it (the row's label); pressing
/// "Try again" has none, and inventing some would put words in their mouth on reload.
/// </para>
/// </summary>
public sealed class AiControllerRetryTests : IDisposable
{
    private const string Arguments = """{"query":"Al Safa Park 2"}""";

    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly ISelectedActionResolver _selectedActionResolver = Substitute.For<ISelectedActionResolver>();
    private readonly IRetryTargetResolver _retryTargetResolver = Substitute.For<IRetryTargetResolver>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly List<AppendMessageCommand> _appended = [];
    private readonly List<SendChatMessageCommand> _dispatched = [];
    private readonly MemoryStream _responseBody = new();
    private readonly AiController _controller;
    private readonly Guid _chatId = Guid.NewGuid();
    private readonly Guid _failedMessageId = Guid.NewGuid();

    public AiControllerRetryTests()
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

        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _dispatched.Add(call.Arg<SendChatMessageCommand>()!);
                return SucceedingRetryTurn();
            });

        var turnRecorder = new TurnRecorder(
            Substitute.For<IAgentRepository>(), Substitute.For<IAgentExecutionRepository>(),
            Substitute.For<IUnitOfWork>(), NullLogger<TurnRecorder>.Instance);

        _controller = new AiController(_mediator, _providers, _models, _selectedActionResolver,
            _retryTargetResolver,
            Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions()),
            turnRecorder, _currentUser, NullLogger<AiController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Response = { Body = _responseBody } },
            },
        };
    }

    public void Dispose() => _responseBody.Dispose();

    private static async IAsyncEnumerable<ChatStreamChunk> SucceedingRetryTurn()
    {
        yield return new ChatStreamChunk("Trying that again now — Al Safa Park 2.", null);
        yield return new ChatStreamChunk("I've shown you Al Safa Park 2 on the map.", null);
        yield return new ChatStreamChunk(null, null, TurnOutcome: RecordedTurnOutcome.Acted(
            [ActionAttempt.Success("Capability", "resolve_location", "Al Safa Park 2", Arguments)],
            DateTimeOffset.UtcNow));
        await Task.CompletedTask;
    }

    private void ResolverFinds(string key = "resolve_location", string? targetLabel = "Al Safa Park 2") =>
        _retryTargetResolver.ResolveAsync(_chatId, _failedMessageId, Arg.Any<CancellationToken>())
            .Returns(new RetryTarget(
                _failedMessageId,
                ActionAttempt.Failure("Capability", key, targetLabel, Arguments, "the provider was unavailable")));

    private Task RetryAsync() => _controller.Chat(
        new ChatRequest(_chatId, [], Guid.NewGuid(), Guid.NewGuid(), null, null, new RetryRequest(_failedMessageId)),
        CancellationToken.None);

    [Fact]
    public async Task ARetry_ShouldNotInsertAUserMessageNobodyTyped()
    {
        ResolverFinds();

        await RetryAsync();

        // FR-013b/SC-012 — on reload the transcript shows the failed turn, then the retry's own
        // assistant turn, and nothing in between.
        _appended.Should().NotContain(c => c.Role == MessageRole.User);
        _appended.Should().ContainSingle().Which.Role.Should().Be(MessageRole.Assistant);
    }

    [Fact]
    public async Task ARetriedTurn_ShouldPersistItsOwnOutcome()
    {
        ResolverFinds();

        await RetryAsync();

        // Its own, not the failed turn's: the first attempt keeps the failure it recorded, which is
        // what leaves the transcript able to show that it did not work (FR-013a).
        var assistant = _appended.Should().ContainSingle().Subject;
        assistant.TurnOutcomeJson.Should().NotBeNull();
        assistant.TurnOutcomeJson.Should().Contain("resolve_location");
    }

    [Fact]
    public async Task ARetry_ShouldTakeEveryParameterFromTheServersOwnRecord()
    {
        ResolverFinds();

        await RetryAsync();

        // FR-010 — the request carried a message id and nothing else; the capability, its arguments
        // and its target all came back from the resolver. This is what stops the endpoint being a
        // general capability-invocation route wearing a retry's clothes.
        var retry = _dispatched.Should().ContainSingle().Subject.Retry;
        retry.Should().NotBeNull();
        retry!.CapabilityKey.Should().Be("resolve_location");
        retry.ArgumentsJson.Should().Be(Arguments);
        retry.TargetLabel.Should().Be("Al Safa Park 2");
        retry.PreviousFailureReason.Should().Be("the provider was unavailable");

        await _retryTargetResolver.Received(1).ResolveAsync(_chatId, _failedMessageId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARetriedTurnsAccount_ShouldNotRepeatTheFirstFailureNotice()
    {
        ResolverFinds();

        await RetryAsync();

        // FR-011/SC-005 — the user has to be able to tell which attempt they are reading.
        Encoding.UTF8.GetString(_responseBody.ToArray()).Should().Contain("again");
    }

    [Fact]
    public async Task ARequestCarryingBothASelectionAndARetry_ShouldBeRejected()
    {
        ResolverFinds();

        var chat = async () => await _controller.Chat(
            new ChatRequest(
                _chatId, [new ChatMessageDto("user", "try again")], Guid.NewGuid(), Guid.NewGuid(), null,
                new SelectedActionRequest(Guid.NewGuid(), "capability", "resolve_location", "Show it", null),
                new RetryRequest(_failedMessageId)),
            CancellationToken.None);

        // Two instructions for one turn with no defensible order between them. 400 via
        // ProblemDetailsMiddleware; picking one silently is the guessing this feature removes.
        await chat.Should().ThrowAsync<DomainRuleViolationException>();

        _appended.Should().BeEmpty("nothing is persisted for a request that was never valid");
    }

    [Fact]
    public async Task AnUnresolvableRetry_ShouldSurfaceBeforeTheStreamStarts()
    {
        _retryTargetResolver.ResolveAsync(_chatId, _failedMessageId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConversationActionStaleException("That already succeeded."));

        var chat = async () => await RetryAsync();

        // Resolved before Response.ContentType is set, so the client gets Problem Details rather
        // than a half-open SSE stream it has to interpret — the same rule selected actions follow.
        await chat.Should().ThrowAsync<ConversationActionStaleException>();

        _responseBody.Length.Should().Be(0);
        _appended.Should().BeEmpty();
    }
}
