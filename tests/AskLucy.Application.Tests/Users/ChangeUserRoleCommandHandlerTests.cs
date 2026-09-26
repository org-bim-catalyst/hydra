using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization.Assignments.Commands.AssignRole;
using AskLucy.Application.Users.Commands.ChangeUserRole;
using AskLucy.Domain.Authorization;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Users;

/// <summary>
/// specs/055-role-management: this handler is now a thin legacy-contract adapter — it resolves
/// a role *name* (built-in name or the "Regular" sentinel) to a role id and delegates to
/// <see cref="AssignRoleCommand"/> via <see cref="ISender"/>, which is where FR-014/FR-016's
/// privileged-role rule and FR-023/FR-017's last-Super-User safeguard now live (and are
/// thoroughly tested — see AssignRoleCommandHandlerTests). These tests verify only the
/// adapter's own job: correct name resolution and delegation, plus its legacy logging.
/// </summary>
public sealed class ChangeUserRoleCommandHandlerTests
{
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly IRoleAssignmentRepository _roleAssignmentRepository = Substitute.For<IRoleAssignmentRepository>();
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly ChangeUserRoleCommandHandler _handler;

    public ChangeUserRoleCommandHandlerTests()
    {
        _currentUser.UserId.Returns("actor-1");
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns([]);
        _roleAssignmentRepository.GetCurrentRoleIdAsync("target-1", Arg.Any<CancellationToken>()).Returns((string?)null);
        _handler = new ChangeUserRoleCommandHandler(
            _mediator, _roleRepository, _roleAssignmentRepository, _identityService, _currentUser,
            Substitute.For<ILogger<ChangeUserRoleCommandHandler>>());
    }

    [Fact]
    public async Task Handle_LegacyRegularName_ResolvesToTheUserRole()
    {
        _roleRepository.GetByNormalizedNameAsync("USER", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("user-id", "User", null, true, PermissionSet.Empty, 3, null, "stamp"));

        await _handler.Handle(new ChangeUserRoleCommand("target-1", "Regular"), CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<AssignRoleCommand>(c => c!.UserId == "target-1" && c.RoleId == "user-id"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_BuiltInRoleName_ResolvesToIdAndDelegates()
    {
        _roleRepository.GetByNormalizedNameAsync("SUPER USER", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("super-user-id", "Super User", null, true, PermissionSet.Full, 3, null, "stamp"));

        await _handler.Handle(new ChangeUserRoleCommand("target-1", "Super User"), CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<AssignRoleCommand>(c => c!.UserId == "target-1" && c.RoleId == "super-user-id"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownRoleName_ThrowsKeyNotFound()
    {
        _roleRepository.GetByNormalizedNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((RoleRecord?)null);

        var act = () => _handler.Handle(new ChangeUserRoleCommand("target-1", "Ghost Role"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_PassesTheFreshlyReadCurrentRoleIdAsTheExpectedConcurrencyToken()
    {
        _roleAssignmentRepository.GetCurrentRoleIdAsync("target-1", Arg.Any<CancellationToken>()).Returns("previous-role-id");
        _roleRepository.GetByNormalizedNameAsync("ADMINISTRATOR", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("admin-id", "Administrator", null, true, PermissionSet.Full, 1, null, "stamp"));

        await _handler.Handle(new ChangeUserRoleCommand("target-1", "Administrator"), CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<AssignRoleCommand>(c => c!.ExpectedCurrentRoleId == "previous-role-id"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PropagatesAssignRoleCommandFailures()
    {
        _roleRepository.GetByNormalizedNameAsync("SUPER USER", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("super-user-id", "Super User", null, true, PermissionSet.Full, 3, null, "stamp"));
        _mediator.Send(Arg.Any<AssignRoleCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new UnauthorizedAccessException("Only a Super User can grant or revoke the Administrator or Super User role."));

        var act = () => _handler.Handle(new ChangeUserRoleCommand("target-1", "Super User"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
