using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization;
using AskLucy.Application.Authorization.Assignments.Commands.AssignRole;
using AskLucy.Application.Users;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.Common;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authorization;

/// <summary>
/// FR-016 privilege-escalation guard and FR-017/FR-023 last-Super-User guard, generalized to
/// any role (built-in or custom) — the single implementation
/// <see cref="AskLucy.Application.Users.Commands.ChangeUserRole.ChangeUserRoleCommandHandler"/>
/// also delegates to (ports the scenarios from the pre-refactor ChangeUserRoleCommandHandlerTests,
/// research.md Decision 8, plan.md T085/T086 — SC-007).
/// </summary>
public sealed class AssignRoleCommandHandlerTests
{
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly IRoleAssignmentRepository _assignmentRepository = Substitute.For<IRoleAssignmentRepository>();
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly AssignRoleCommandHandler _handler;

    private static readonly RoleRecord SuperUserRole = new("su-id", PrivilegedRoleNames.SuperUser, null, true, PermissionSet.Full, 0, null, "s");
    private static readonly RoleRecord AdministratorRole = new("admin-id", PrivilegedRoleNames.Administrator, null, true, PermissionSet.Full, 0, null, "s");
    private static readonly RoleRecord CustomRole = new("custom-id", "Moderator", null, false, PermissionSet.Create("admin.dashboard.view"), 0, null, "s");
    private static readonly RoleRecord UserRole = new("user-id", DefaultRole.Name, null, true, PermissionSet.Empty, 0, null, "s");

    public AssignRoleCommandHandlerTests()
    {
        _currentUser.UserId.Returns("actor-1");
        _identityService.CountActiveSuperUsersAsync(Arg.Any<CancellationToken>()).Returns(5);
        _assignmentRepository.ReplaceRoleAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ReplaceRoleOutcome.Success);
        _roleRepository.GetByIdAsync("su-id", Arg.Any<CancellationToken>()).Returns(SuperUserRole);
        _roleRepository.GetByIdAsync("admin-id", Arg.Any<CancellationToken>()).Returns(AdministratorRole);
        _roleRepository.GetByIdAsync("custom-id", Arg.Any<CancellationToken>()).Returns(CustomRole);
        _roleRepository.GetByIdAsync("user-id", Arg.Any<CancellationToken>()).Returns(UserRole);
        _roleRepository.GetByNormalizedNameAsync(DefaultRole.NormalizedName, Arg.Any<CancellationToken>()).Returns(UserRole);

        _handler = new AssignRoleCommandHandler(_roleRepository, _assignmentRepository, _identityService, _currentUser);
    }

    [Fact]
    public async Task ShouldReject_WhenPlainAdministratorAttemptsToGrantSuperUser()
    {
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(false);
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns([]);

        var act = () => _handler.Handle(new AssignRoleCommand("target-1", "su-id", null), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await _assignmentRepository.DidNotReceiveWithAnyArgs().ReplaceRoleAsync(default!, default, default, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ShouldReject_WhenPlainAdministratorAttemptsToGrantAdministrator()
    {
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(false);
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns([]);

        var act = () => _handler.Handle(new AssignRoleCommand("target-1", "admin-id", null), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ShouldReject_WhenPlainAdministratorAttemptsToRevokeATargetsSuperUserRole()
    {
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(false);
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns([PrivilegedRoleNames.SuperUser]);

        var act = () => _handler.Handle(new AssignRoleCommand("target-1", null, "su-id"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ShouldAllow_WhenPlainAdministratorChangesARegularUsersRole()
    {
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(false);
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns([]);

        await _handler.Handle(new AssignRoleCommand("target-1", "custom-id", null), CancellationToken.None);

        await _assignmentRepository.Received(1).ReplaceRoleAsync("target-1", "custom-id", null, "actor-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemovingARole_MovesTheUserToTheUserRole_NeverToNoRole()
    {
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(false);
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns(["Moderator"]);

        await _handler.Handle(new AssignRoleCommand("target-1", null, "custom-id"), CancellationToken.None);

        await _assignmentRepository.Received(1).ReplaceRoleAsync("target-1", "user-id", "custom-id", "actor-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemovingARole_Throws_WhenTheUserRoleIsMissing()
    {
        _roleRepository.GetByNormalizedNameAsync(DefaultRole.NormalizedName, Arg.Any<CancellationToken>()).Returns((RoleRecord?)null);

        var act = () => _handler.Handle(new AssignRoleCommand("target-1", null, "custom-id"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _assignmentRepository.DidNotReceiveWithAnyArgs().ReplaceRoleAsync(default!, default, default, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ShouldAllow_WhenSuperUserGrantsSuperUser()
    {
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(true);
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns([]);

        await _handler.Handle(new AssignRoleCommand("target-1", "su-id", null), CancellationToken.None);

        await _assignmentRepository.Received(1).ReplaceRoleAsync("target-1", "su-id", null, "actor-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShouldReject_WhenDemotingTheLastActiveSuperUser()
    {
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(true);
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns([PrivilegedRoleNames.SuperUser]);
        _identityService.CountActiveSuperUsersAsync(Arg.Any<CancellationToken>()).Returns(1);

        var act = () => _handler.Handle(new AssignRoleCommand("target-1", null, "su-id"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await _assignmentRepository.DidNotReceiveWithAnyArgs().ReplaceRoleAsync(default!, default, default, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ShouldAllow_WhenTargetIsTheLastActiveSuperUserButRoleStaysSuperUser()
    {
        // Reassigning the same Super User role to the last Super User isn't a removal.
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(true);
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns([PrivilegedRoleNames.SuperUser]);
        _identityService.CountActiveSuperUsersAsync(Arg.Any<CancellationToken>()).Returns(1);

        await _handler.Handle(new AssignRoleCommand("target-1", "su-id", "su-id"), CancellationToken.None);

        await _assignmentRepository.Received(1).ReplaceRoleAsync("target-1", "su-id", "su-id", "actor-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownRoleId_ThrowsKeyNotFound()
    {
        _roleRepository.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((RoleRecord?)null);

        var act = () => _handler.Handle(new AssignRoleCommand("target-1", "missing", null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_RepositoryReportsConcurrencyMismatch_ThrowsConcurrencyConflictException()
    {
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns([]);
        _assignmentRepository.ReplaceRoleAsync("target-1", "custom-id", "stale-id", "actor-1", Arg.Any<CancellationToken>())
            .Returns(ReplaceRoleOutcome.ConcurrencyMismatch);

        var act = () => _handler.Handle(new AssignRoleCommand("target-1", "custom-id", "stale-id"), CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Handle_RepositoryReportsUserLocked_ThrowsDomainRuleViolation()
    {
        _identityService.GetRolesAsync("target-1", Arg.Any<CancellationToken>()).Returns([]);
        _assignmentRepository.ReplaceRoleAsync("target-1", "custom-id", null, "actor-1", Arg.Any<CancellationToken>())
            .Returns(ReplaceRoleOutcome.UserLocked);

        var act = () => _handler.Handle(new AssignRoleCommand("target-1", "custom-id", null), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }
}
