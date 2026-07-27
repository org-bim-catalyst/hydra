using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.Translate;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

public sealed class TranslateCommandHandlerTests
{
    private readonly IAIProvider _aiProvider = Substitute.For<IAIProvider>();
    private readonly TranslateCommandHandler _handler;

    public TranslateCommandHandlerTests() => _handler = new TranslateCommandHandler(_aiProvider);

    [Fact]
    public async Task Handle_ShouldExtractHtmlBlock_WhenProviderWrapsResponseInHtmlFence()
    {
        _aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns("Sure, here you go:\n```html\n<span lang=\"fr\">Bonjour</span>\n```\nHope that helps!");

        var result = await _handler.Handle(new TranslateCommand("Hello", "French"), CancellationToken.None);

        result.Should().Be("<span lang=\"fr\">Bonjour</span>");
    }

    [Fact]
    public async Task Handle_ShouldReturnRawResponse_WhenNoHtmlFencePresent()
    {
        _aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns("Bonjour");

        var result = await _handler.Handle(new TranslateCommand("Hello", "French"), CancellationToken.None);

        result.Should().Be("Bonjour");
    }
}
