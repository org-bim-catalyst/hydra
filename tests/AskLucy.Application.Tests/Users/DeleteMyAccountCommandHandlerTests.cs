using AskLucy.Application.Abstractions;
using AskLucy.Application.Users.Commands.DeleteMyAccount;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Users;

public sealed class DeleteMyAccountCommandHandlerTests
{
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly DeleteMyAccountCommandHandler _handler;

    public DeleteMyAccountCommandHandlerTests()
    {
        _currentUser.UserId.Returns("user-1");
        _handler = new DeleteMyAccountCommandHandler(_identityService, _currentUser);
    }

    [Fact]
    public async Task Handle_ShouldDeleteAccount_WhenPasswordIsCorrect()
    {
        _identityService.VerifyPasswordAsync("user-1", "Password1!", Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.Handle(new DeleteMyAccountCommand("Password1!"), CancellationToken.None);

        result.Status.Should().Be(IdentityResultStatus.Success);
        await _identityService.Received(1).DeleteAsync("user-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnFailed_AndNeverDelete_WhenPasswordIsIncorrect()
    {
        _identityService.VerifyPasswordAsync("user-1", "wrong", Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.Handle(new DeleteMyAccountCommand("wrong"), CancellationToken.None);

        result.Status.Should().Be(IdentityResultStatus.Failed);
        result.Errors.Should().Contain("Incorrect password.");
        await _identityService.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }
}
