using System.Security.Claims;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication.Commands.ExternalLogin;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authentication;

public sealed class ProcessExternalLoginCallbackCommandHandlerTests
{
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly IExternalLoginCodeStore _codeStore = Substitute.For<IExternalLoginCodeStore>();
    private readonly ProcessExternalLoginCallbackCommandHandler _handler;

    public ProcessExternalLoginCallbackCommandHandlerTests()
    {
        _handler = new ProcessExternalLoginCallbackCommandHandler(_identityService, _codeStore);
    }

    [Fact]
    public async Task Handle_ShouldIssueCompletionCode_WhenResolutionSucceeds()
    {
        _identityService.ResolveExternalLoginAsync("Google", "provider-key-1", "user@example.com", true, null, Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success, "user-1", [new Claim(ClaimTypes.NameIdentifier, "user-1")]));
        _codeStore.Issue("user-1", Arg.Any<TimeSpan>()).Returns("completion-code");

        var code = await _handler.Handle(
            new ProcessExternalLoginCallbackCommand("Google", "provider-key-1", "user@example.com", true, null),
            CancellationToken.None);

        code.Should().Be("completion-code");
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenResolutionFails()
    {
        _identityService.ResolveExternalLoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Failed, Errors: ["nope"]));

        var code = await _handler.Handle(
            new ProcessExternalLoginCallbackCommand("Google", "provider-key-1", "user@example.com", true, null),
            CancellationToken.None);

        code.Should().BeNull();
        _codeStore.DidNotReceiveWithAnyArgs().Issue(default!, default);
    }

    [Fact]
    public async Task Handle_ShouldPassLinkToUserId_WhenLinking()
    {
        _identityService.ResolveExternalLoginAsync("Facebook", "provider-key-2", null, false, "existing-user", Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success, "existing-user", []));
        _codeStore.Issue("existing-user", Arg.Any<TimeSpan>()).Returns("link-code");

        var code = await _handler.Handle(
            new ProcessExternalLoginCallbackCommand("Facebook", "provider-key-2", null, false, "existing-user"),
            CancellationToken.None);

        code.Should().Be("link-code");
    }
}
