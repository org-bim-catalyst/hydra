using AskLucy.Application.Abstractions;
using AskLucy.Application.Users.Commands.ForceReset2fa;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Users;

public sealed class ForceReset2faCommandHandlerTests
{
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly ForceReset2faCommandHandler _handler;

    public ForceReset2faCommandHandlerTests()
    {
        _currentUser.UserId.Returns("admin-1");
        _handler = new ForceReset2faCommandHandler(_identityService, _currentUser, Substitute.For<ILogger<ForceReset2faCommandHandler>>());
    }

    [Fact]
    public async Task ShouldRejectSelfTargeting()
    {
        var act = () => _handler.Handle(new ForceReset2faCommand("admin-1"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await _identityService.DidNotReceiveWithAnyArgs().DisableTwoFactorAsync(default!, default);
    }

    [Fact]
    public async Task ShouldDelegateToDisableTwoFactorAsync_WhenTargetingSomeoneElse()
    {
        await _handler.Handle(new ForceReset2faCommand("user-2"), CancellationToken.None);

        await _identityService.Received(1).DisableTwoFactorAsync("user-2", Arg.Any<CancellationToken>());
    }
}
