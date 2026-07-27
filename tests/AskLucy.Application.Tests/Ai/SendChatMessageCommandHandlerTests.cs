using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

public sealed class SendChatMessageCommandHandlerTests
{
    private readonly IAIProvider _aiProvider = Substitute.For<IAIProvider>();
    private readonly SendChatMessageCommandHandler _handler;

    public SendChatMessageCommandHandlerTests()
    {
        _handler = new SendChatMessageCommandHandler(_aiProvider, new SendChatMessageCommandValidator());
    }

    [Fact]
    public async Task Handle_ShouldYieldProviderChunks_WhenMessagesAreValid()
    {
        _aiProvider.StreamChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(["Hello", " world"]));

        var command = new SendChatMessageCommand([new ChatMessageDto("user", "Hi")]);

        var chunks = new List<string>();
        await foreach (var chunk in _handler.Handle(command, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        chunks.Should().Equal("Hello", " world");
    }

    [Fact]
    public async Task Handle_ShouldThrowValidationException_WhenMessagesAreEmpty()
    {
        var command = new SendChatMessageCommand([]);

        var act = async () =>
        {
            await foreach (var _ in _handler.Handle(command, CancellationToken.None))
            {
            }
        };

        await act.Should().ThrowAsync<ValidationException>();
    }

    private static async IAsyncEnumerable<string> ToAsyncEnumerable(IEnumerable<string> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
