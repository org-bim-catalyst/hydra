using System.Text;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Chats;
using AskLucy.Application.Chats.Commands.AppendMessage;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.Locations;
using AskLucy.Application.Options;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Conversations;
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
/// specs/044-location-viewer-regression T012 (FR-001a, contract C-1) — the assertion the whole fix
/// turns on.
/// <para>
/// The handler yielding its location chunk earlier achieves nothing on its own: this controller
/// used to drain the entire stream before writing any trailing event, so <c>__LOCATION__</c> still
/// reached the client only after the optional boundary step finished — or never, if it threw. Both
/// halves are required, and only a controller-level test can tell them apart.
/// </para>
/// <para>
/// Driven directly against the controller with a fake <see cref="ISender"/> and a
/// <see cref="DefaultHttpContext"/> writing into a <see cref="MemoryStream"/>, rather than through
/// <c>CustomWebApplicationFactory</c>: this is about byte ordering on the wire, not routing or auth.
/// </para>
/// </summary>
public sealed class AiControllerChatStreamTests : IDisposable
{
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly ISelectedActionResolver _selectedActionResolver = Substitute.For<ISelectedActionResolver>();
    private readonly MemoryStream _responseBody = new();
    private readonly AiController _controller;
    private readonly Guid _chatId = Guid.NewGuid();

    public AiControllerChatStreamTests()
    {
        _mediator.Send(Arg.Any<AppendMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(new MessageDto(
                Guid.NewGuid(), "assistant", "text", "Here you go.", null, DateTime.UtcNow,
                null, null, null, null, null, null, null, null, null, [], []));

        _controller = new AiController(_mediator, _providers, _models, _selectedActionResolver,
            Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions()),
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

    /// <summary>
    /// The stream yields the location chunk, then a boundary chunk. At the moment the boundary
    /// chunk is produced, <c>__LOCATION__</c> must ALREADY be on the wire — proving the controller
    /// flushed it mid-stream rather than after the loop drained.
    /// </summary>
    [Fact]
    public async Task Chat_ShouldFlushTheLocationEvent_BeforeTheBoundaryChunkIsEvenProduced()
    {
        var confirmedLocation = new ConfirmedLocationData(25.156, 55.2218, "Al Safa Park 2", 0.9);
        var locationWrittenBeforeBoundaryChunk = false;

        async IAsyncEnumerable<ChatStreamChunk> Stream()
        {
            yield return new ChatStreamChunk("Here you go.", null);
            yield return new ChatStreamChunk(null, null, ConfirmedLocation: confirmedLocation);

            // Resumed only after the controller has handled the chunk above.
            locationWrittenBeforeBoundaryChunk = ResponseText().Contains("__LOCATION__", StringComparison.Ordinal);

            await Task.CompletedTask;
        }

        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>()).Returns(Stream());

        await _controller.Chat(
            new ChatRequest(_chatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], Guid.NewGuid(), Guid.NewGuid(), null),
            CancellationToken.None);

        locationWrittenBeforeBoundaryChunk.Should().BeTrue(
            "__LOCATION__ must reach the client before the optional boundary step runs, not after the stream drains");
    }

    /// <summary>
    /// 2026-09-11 live-testing report: users intermittently saw "Incomplete — connection
    /// dropped" for what was often an ordinary, recoverable mid-stream failure. Root cause:
    /// nothing wrapped the streaming loop in a try/catch, so an exception thrown after the
    /// response had already started (headers sent, text/event-stream in progress) propagated
    /// uncaught — ASP.NET Core cannot turn that into a normal error response, so it just resets
    /// the connection, which is exactly what the client's fetch reader reports as a raw network
    /// error (constitution §2.VIII: this was a silent-failure gap, not intentional behavior).
    /// This test proves the fix: the exception is caught, the stream still ends cleanly with
    /// `[DONE]`, and the partial reply plus a plain explanation reach the client instead of a
    /// severed connection.
    /// </summary>
    [Fact]
    public async Task Chat_ShouldEndTheStreamCleanly_WhenTheHandlerThrowsMidStream()
    {
        async IAsyncEnumerable<ChatStreamChunk> Stream()
        {
            yield return new ChatStreamChunk("Here you go", null);
            await Task.Yield();
            throw new InvalidOperationException("simulated mid-stream failure");
#pragma warning disable CS0162 // Unreachable code — required to satisfy the iterator's yield-based signature.
            yield break;
#pragma warning restore CS0162
        }

        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>()).Returns(Stream());

        var act = async () => await _controller.Chat(
            new ChatRequest(_chatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], Guid.NewGuid(), Guid.NewGuid(), null),
            CancellationToken.None);

        await act.Should().NotThrowAsync("an exception after the response starts must be caught, never left to reset the connection");
        ResponseText().Should().Contain("Here you go");
        ResponseText().Should().Contain("Something went wrong partway through and I couldn't finish");
        ResponseText().Should().Contain("data: [DONE]");
        await _mediator.Received(1).Send(
            Arg.Is<AppendMessageCommand>(c => c != null && c.Content.Contains("Here you go") && c.Content.Contains("Something went wrong")),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// FR-002 / contract C-4: the turn still terminates cleanly and the viewer has still been told
    /// where to go, even though the boundary step blew up mid-stream.
    /// </summary>
    [Fact]
    public async Task Chat_ShouldStillHaveWrittenTheLocationEvent_WhenTheStreamFaultsAfterIt()
    {
        var confirmedLocation = new ConfirmedLocationData(25.156, 55.2218, "Al Safa Park 2", 0.9);

        async IAsyncEnumerable<ChatStreamChunk> Stream()
        {
            yield return new ChatStreamChunk(null, null, ConfirmedLocation: confirmedLocation);
            await Task.CompletedTask;
            throw new HttpRequestException("boundary/vision blew up after the location was emitted");
        }

        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>()).Returns(Stream());

        var act = async () => await _controller.Chat(
            new ChatRequest(_chatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], Guid.NewGuid(), Guid.NewGuid(), null),
            CancellationToken.None);

        // 2026-09-11: the controller now catches a mid-stream fault itself rather than letting it
        // escape and reset the connection (see Chat_ShouldEndTheStreamCleanly_WhenTheHandlerThrowsMidStream)
        // — the narrower controller-level property this test still guards is unchanged: whatever
        // happens later, the viewer was already told.
        await act.Should().NotThrowAsync();
        ResponseText().Should().Contain("__LOCATION__");
        ResponseText().Should().Contain("Al Safa Park 2");
    }

    [Fact]
    public async Task Chat_ShouldWriteTheLocationEventExactlyOnce_AndCompleteWithDone()
    {
        var confirmedLocation = new ConfirmedLocationData(25.156, 55.2218, "Al Safa Park 2", 0.9);

        async IAsyncEnumerable<ChatStreamChunk> Stream()
        {
            yield return new ChatStreamChunk("Here you go.", null);
            yield return new ChatStreamChunk(null, null, ConfirmedLocation: confirmedLocation);
            await Task.CompletedTask;
        }

        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>()).Returns(Stream());

        await _controller.Chat(
            new ChatRequest(_chatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], Guid.NewGuid(), Guid.NewGuid(), null),
            CancellationToken.None);

        var text = ResponseText();
        text.Split("__LOCATION__").Should().HaveCount(2, "the location event must not be written twice by the mid-stream move");
        text.Should().Contain("data: [DONE]");
    }

    /// <summary>
    /// The boundary confirmation reports a second action, finishing seconds after the location
    /// did. Appended to the reply it ran two unrelated sentences together and rewrote a bubble the
    /// user had already read, so a chunk can now ask for a message break — and the break has to be
    /// on the wire before the first character that belongs to the new message.
    /// </summary>
    [Fact]
    public async Task Chat_ShouldWriteTheMessageBreak_BeforeTheContentThatBelongsToTheNewMessage()
    {
        async IAsyncEnumerable<ChatStreamChunk> Stream()
        {
            yield return new ChatStreamChunk("Centred the viewer on it.", null);
            yield return new ChatStreamChunk("I have outlined the site boundary.", null, StartsNewMessage: true);
            await Task.CompletedTask;
        }

        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>()).Returns(Stream());

        await _controller.Chat(
            new ChatRequest(_chatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], Guid.NewGuid(), Guid.NewGuid(), null),
            CancellationToken.None);

        var text = ResponseText();
        text.IndexOf("__MESSAGE_BREAK__", StringComparison.Ordinal).Should().BeGreaterThan(
            text.IndexOf("Centred the viewer on it.", StringComparison.Ordinal),
            "the first message is complete before the break is announced");
        text.IndexOf("__MESSAGE_BREAK__", StringComparison.Ordinal).Should().BeLessThan(
            text.IndexOf("I have outlined the site boundary.", StringComparison.Ordinal),
            "the client must have opened the new bubble before its first character arrives");
    }

    /// <summary>
    /// 2026-09-11 live-testing report: "after a page reload, any offer card stored in the chat
    /// history appears empty." Root cause: the persisted <c>SuggestedActionsJson</c> serialized
    /// the raw <see cref="SuggestedAction"/> record (PascalCase — <c>Label</c>, <c>Key</c>,
    /// <c>IsDecline</c>...), while <c>useChatStream.ts</c>'s <c>toOfferFields</c> — and
    /// <c>SuggestedActionCard</c> itself — read the camelCase shape the live <c>__ACTIONS__</c>
    /// event already used (<c>label</c>, <c>capabilityKey</c>, <c>isDecline</c>...). Every field
    /// on a reopened offer read as <c>undefined</c>. This asserts the persisted JSON now matches
    /// the live event's exact shape.
    /// </summary>
    [Fact]
    public async Task Chat_ShouldPersistSuggestedActionsJson_InTheSameShapeAsTheLiveActionsEvent()
    {
        var actions = new[]
        {
            new SuggestedAction(SuggestedActionKind.FlowVariant, "locate_a_place", null, "Find and outline", "Locate it and highlight the boundary", null),
        };

        async IAsyncEnumerable<ChatStreamChunk> Stream()
        {
            yield return new ChatStreamChunk("Here it is.", null, SuggestedActions: actions, SuggestedActionsQuestion: "Want more detail?");
            await Task.CompletedTask;
        }

        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>()).Returns(Stream());

        await _controller.Chat(
            new ChatRequest(_chatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], Guid.NewGuid(), Guid.NewGuid(), null),
            CancellationToken.None);

        var persistedJson = _mediator.ReceivedCalls()
            .Select(call => call.GetArguments().FirstOrDefault())
            .OfType<AppendMessageCommand>()
            .Where(command => command.Role == MessageRole.Assistant)
            .Select(command => command.SuggestedActionsJson)
            .Should().ContainSingle(json => json != null).Subject!;

        using var document = JsonDocument.Parse(persistedJson);
        var action = document.RootElement.GetProperty("actions")[0];
        action.GetProperty("label").GetString().Should().Be("Find and outline");
        action.GetProperty("description").GetString().Should().Be("Locate it and highlight the boundary");
        action.GetProperty("capabilityKey").GetString().Should().Be("locate_a_place");
        action.GetProperty("isDecline").GetBoolean().Should().BeFalse();
        action.TryGetProperty("Label", out _).Should().BeFalse("the persisted shape must never regress to raw PascalCase");
    }

    [Fact]
    public async Task Chat_ShouldPersistTwoAssistantMessages_WhenAChunkStartsANewOne()
    {
        async IAsyncEnumerable<ChatStreamChunk> Stream()
        {
            yield return new ChatStreamChunk("Centred the viewer on it.", null);
            yield return new ChatStreamChunk("I have outlined the site boundary.", null, StartsNewMessage: true);
            await Task.CompletedTask;
        }

        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>()).Returns(Stream());

        await _controller.Chat(
            new ChatRequest(_chatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], Guid.NewGuid(), Guid.NewGuid(), null),
            CancellationToken.None);

        var assistantContents = _mediator.ReceivedCalls()
            .Select(call => call.GetArguments().FirstOrDefault())
            .OfType<AppendMessageCommand>()
            .Where(command => command.Role == MessageRole.Assistant)
            .Select(command => command.Content)
            .ToList();

        assistantContents.Should().Equal("Centred the viewer on it.", "I have outlined the site boundary.");
    }

    /// <summary>
    /// The break carries what the newly-opened message is waiting for, so the client can say so
    /// instead of leaving a finished-looking reply silent for tens of seconds.
    /// </summary>
    [Fact]
    public async Task Chat_ShouldWriteThePendingLabel_OnABreakThatAnnouncesUnfinishedWork()
    {
        async IAsyncEnumerable<ChatStreamChunk> Stream()
        {
            yield return new ChatStreamChunk("Centred the viewer on it.", null);
            yield return new ChatStreamChunk(null, null, StartsNewMessage: true, PendingLabel: "Finding the site boundary");
            yield return new ChatStreamChunk("I have outlined the site boundary.", null);
            await Task.CompletedTask;
        }

        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>()).Returns(Stream());

        await _controller.Chat(
            new ChatRequest(_chatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], Guid.NewGuid(), Guid.NewGuid(), null),
            CancellationToken.None);

        var text = ResponseText();
        text.Should().Contain(@"__MESSAGE_BREAK__{""pendingLabel"":""Finding the site boundary""}");
        text.IndexOf("__MESSAGE_BREAK__", StringComparison.Ordinal).Should().BeLessThan(
            text.IndexOf("I have outlined", StringComparison.Ordinal),
            "the label has to reach the client before the work it describes finishes");
    }

    /// <summary>
    /// A break that arrives with nothing buffered must not persist an empty message.
    /// </summary>
    /// <remarks>
    /// The break itself is still written. A chunk may open a message purely to say what it is
    /// waiting for — "Finding the site boundary" — so that the reply can be shown as finished and
    /// spoken while the slow work runs, instead of after it. Only the persist is conditional.
    /// </remarks>
    [Fact]
    public async Task Chat_ShouldNotPersistAnEmptyMessage_WhenTheBreakArrivesWithNothingBuffered()
    {
        async IAsyncEnumerable<ChatStreamChunk> Stream()
        {
            yield return new ChatStreamChunk("I have outlined the site boundary.", null, StartsNewMessage: true);
            await Task.CompletedTask;
        }

        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>()).Returns(Stream());

        await _controller.Chat(
            new ChatRequest(_chatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], Guid.NewGuid(), Guid.NewGuid(), null),
            CancellationToken.None);

        var assistantContents = _mediator.ReceivedCalls()
            .Select(call => call.GetArguments().FirstOrDefault())
            .OfType<AppendMessageCommand>()
            .Where(command => command.Role == MessageRole.Assistant)
            .Select(command => command.Content)
            .ToList();

        assistantContents.Should().Equal("I have outlined the site boundary.");
        ResponseText().Should().Contain("__MESSAGE_BREAK__");
    }
}

/// <summary>
/// specs/045-conversational-agent-runtime research.md D5 (T049) — the keep-alive comment written
/// while a beat sits pending. A separate fixture from <see cref="AiControllerChatStreamTests"/>
/// because it needs a short interval to keep the test fast; the shared fixture's default
/// (production) interval would make every test in this file wait on it for nothing.
/// </summary>
public sealed class AiControllerKeepAliveTests : IDisposable
{
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly ISelectedActionResolver _selectedActionResolver = Substitute.For<ISelectedActionResolver>();
    private readonly MemoryStream _responseBody = new();
    private readonly AiController _controller;
    private readonly Guid _chatId = Guid.NewGuid();

    public AiControllerKeepAliveTests()
    {
        _mediator.Send(Arg.Any<AppendMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(new MessageDto(
                Guid.NewGuid(), "assistant", "text", "Here you go.", null, DateTime.UtcNow,
                null, null, null, null, null, null, null, null, null, [], []));

        // The minimum the [Range] on ConversationRuntimeOptions allows — short enough to keep
        // this test under two seconds while still exercising the real Task.Delay race rather than
        // a mocked clock, which is what actually proves the wire format (a comment line, invisible
        // to aiApi.ts's parser) is correct.
        _controller = new AiController(_mediator, _providers, _models, _selectedActionResolver,
            Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions { KeepAliveIntervalSeconds = 1 }),
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

    [Fact]
    public async Task Chat_ShouldWriteAKeepAliveComment_WhileABeatSitsPendingLongerThanTheInterval()
    {
        async IAsyncEnumerable<ChatStreamChunk> Stream()
        {
            yield return new ChatStreamChunk(null, null, StartsNewMessage: true, PendingLabel: "Highlighting the boundary");

            // Longer than the 1s interval configured above, so at least one keep-alive must be
            // written before this chunk is even produced.
            await Task.Delay(TimeSpan.FromSeconds(1.5));

            yield return new ChatStreamChunk("Done.", null);
        }

        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>()).Returns(Stream());

        await _controller.Chat(
            new ChatRequest(_chatId, [new ChatMessageDto("user", "outline the site")], Guid.NewGuid(), Guid.NewGuid(), null),
            CancellationToken.None);

        ResponseText().Should().Contain(": keep-alive\n\n");
    }

    [Fact]
    public async Task Chat_ShouldNotWriteAKeepAliveComment_WhenChunksArriveFasterThanTheInterval()
    {
        // The other half of the guarantee: a keep-alive is written only when nothing else already
        // reset the connection's idle clock. A chatty stream must not be padded with noise.
        async IAsyncEnumerable<ChatStreamChunk> Stream()
        {
            yield return new ChatStreamChunk("One.", null);
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            yield return new ChatStreamChunk(" Two.", null);
        }

        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>()).Returns(Stream());

        await _controller.Chat(
            new ChatRequest(_chatId, [new ChatMessageDto("user", "hello")], Guid.NewGuid(), Guid.NewGuid(), null),
            CancellationToken.None);

        ResponseText().Should().NotContain(": keep-alive");
    }

    [Fact]
    public async Task Chat_ShouldNotDisturbTheRealChunkSequence_WhenAKeepAliveWasWritten()
    {
        async IAsyncEnumerable<ChatStreamChunk> Stream()
        {
            yield return new ChatStreamChunk(null, null, StartsNewMessage: true, PendingLabel: "Working on it");
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            yield return new ChatStreamChunk("The real content, unaffected by the wait.", null);
        }

        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>()).Returns(Stream());

        await _controller.Chat(
            new ChatRequest(_chatId, [new ChatMessageDto("user", "do the thing")], Guid.NewGuid(), Guid.NewGuid(), null),
            CancellationToken.None);

        var text = ResponseText();
        text.Should().Contain("data: The real content, unaffected by the wait.\n\n");
        text.IndexOf(": keep-alive", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("The real content", StringComparison.Ordinal),
                "the keep-alive must appear while the beat is still pending, before its real content");
    }
}
