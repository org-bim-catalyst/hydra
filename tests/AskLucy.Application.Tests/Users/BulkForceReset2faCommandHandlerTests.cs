using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using AskLucy.Application.Users;
using AskLucy.Application.Users.Commands.BulkForceReset2fa;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Users;

public sealed class BulkForceReset2faCommandHandlerTests
{
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly IUserAdminRepository _userAdminRepository = Substitute.For<IUserAdminRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly BulkForceReset2faCommandHandler _handler;

    public BulkForceReset2faCommandHandlerTests()
    {
        _currentUser.UserId.Returns("actor-1");
        _handler = new BulkForceReset2faCommandHandler(
            _mediator, _identityService, _userAdminRepository, _currentUser,
            Substitute.For<ILogger<BulkForceReset2faCommandHandler>>());
    }

    private static UserAdminDto MakeUser(string id, bool twoFactorEnabled = false) =>
        new(id, $"{id}@example.com", null, null, true, twoFactorEnabled, true, false, "Regular", DateTime.UtcNow);

    [Fact]
    public async Task Handle_NeverErrorsOnAnAlreadyDisabledTarget_StillCountsAsSucceeded()
    {
        _userAdminRepository.GetByIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(MakeUser("user-1", twoFactorEnabled: false));

        var outcome = await _handler.Handle(
            new BulkForceReset2faCommand(new BulkTarget(["user-1"], false), null), CancellationToken.None);

        outcome.SucceededCount.Should().Be(1);
        outcome.Skipped.Should().BeEmpty();
        await _identityService.Received(1).DisableTwoFactorAsync("user-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SkipsSelf()
    {
        var outcome = await _handler.Handle(
            new BulkForceReset2faCommand(new BulkTarget(["actor-1"], false), null), CancellationToken.None);

        outcome.Skipped.Should().ContainSingle(s => s.Id == "actor-1" && s.Reason == "Self");
    }
}
