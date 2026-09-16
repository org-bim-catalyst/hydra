using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using AskLucy.Application.Users.Commands.BulkDeleteUsers;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Users;

public sealed class BulkDeleteUsersCommandHandlerTests
{
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly IUserAdminRepository _userAdminRepository = Substitute.For<IUserAdminRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly BulkDeleteUsersCommandHandler _handler;

    public BulkDeleteUsersCommandHandlerTests()
    {
        _currentUser.UserId.Returns("actor-1");
        _identityService.GetRolesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        _handler = new BulkDeleteUsersCommandHandler(
            _mediator, _identityService, _userAdminRepository, _currentUser,
            Substitute.For<ILogger<BulkDeleteUsersCommandHandler>>());
    }

    [Fact]
    public async Task Handle_DeletesEachResolvedId()
    {
        _userAdminRepository.DeleteAsync("user-1", "actor-1", Arg.Any<CancellationToken>()).Returns(true);

        var outcome = await _handler.Handle(
            new BulkDeleteUsersCommand(new BulkTarget(["user-1"], false), null), CancellationToken.None);

        outcome.SucceededCount.Should().Be(1);
        outcome.Skipped.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SkipsSelf()
    {
        var outcome = await _handler.Handle(
            new BulkDeleteUsersCommand(new BulkTarget(["actor-1"], false), null), CancellationToken.None);

        outcome.Skipped.Should().ContainSingle(s => s.Id == "actor-1" && s.Reason == "Self");
    }

    [Fact]
    public async Task Handle_MirrorsLocksGuards_SkipsLastSuperUser()
    {
        _identityService.GetRolesAsync("super-1", Arg.Any<CancellationToken>()).Returns(["Super User"]);
        _identityService.CountActiveSuperUsersAsync(Arg.Any<CancellationToken>()).Returns(1);

        var outcome = await _handler.Handle(
            new BulkDeleteUsersCommand(new BulkTarget(["super-1"], false), null), CancellationToken.None);

        outcome.Skipped.Should().ContainSingle(s => s.Id == "super-1" && s.Reason == "LastSuperUser");
        await _userAdminRepository.DidNotReceive().DeleteAsync("super-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SkipsNotFound()
    {
        _userAdminRepository.DeleteAsync("ghost", "actor-1", Arg.Any<CancellationToken>()).Returns(false);

        var outcome = await _handler.Handle(
            new BulkDeleteUsersCommand(new BulkTarget(["ghost"], false), null), CancellationToken.None);

        outcome.Skipped.Should().ContainSingle(s => s.Id == "ghost" && s.Reason == "NotFound");
    }
}
