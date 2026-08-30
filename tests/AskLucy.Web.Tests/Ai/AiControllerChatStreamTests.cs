using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Chats;
using AskLucy.Application.Chats.Commands.AppendMessage;
using AskLucy.Application.Locations;
using AskLucy.Web.Contracts;
using AskLucy.Web.Controllers.v1;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    private readonly MemoryStream _responseBody = new();
    private readonly AiController _controller;
    private readonly Guid _chatId = Guid.NewGuid();

    public AiControllerChatStreamTests()
    {
        _mediator.Send(Arg.Any<AppendMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(new MessageDto(
                Guid.NewGuid(), "assistant", "text", "Here you go.", null, DateTime.UtcNow,
                null, null, null, null, null, null, null, null, null, [], []));

        _controller = new AiController(_mediator, _providers, _models)
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

        // The handler is what guarantees this never escapes in production (FR-002); here we assert
        // the narrower controller-level property: whatever happens later, the viewer was already told.
        await act.Should().ThrowAsync<HttpRequestException>();
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
}
