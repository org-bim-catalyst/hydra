using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization;
using AskLucy.Application.Authorization.Assignments.Commands.AssignRole;
using AskLucy.Application.Authorization.Assignments.Commands.BulkAssignRole;
using AskLucy.Application.Authorization.Roles.Commands.BulkDeleteRoles;
using AskLucy.Application.Authorization.Roles.Commands.CreateRole;
using AskLucy.Application.Authorization.Roles.Commands.DeleteRole;
using AskLucy.Application.Authorization.Roles.Commands.UpdateRole;
using AskLucy.Application.Common;
using AskLucy.Application.Users;
using AskLucy.Application.Users.Commands.ChangeUserRole;
using AskLucy.Domain.Authorization;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authorization;

/// <summary>
/// specs/074 T078 (FR-016h–j, SC-010) — every path that could grant, remove, assign, unassign or
/// delete *View user content* refuses an Administrator with the one detail the UI shows, and lets a
/// Super User through under the existing rules. Driven through the real handlers, so a path that
/// forgets to call the guard fails here.
/// </summary>
public sealed class SuperUserControlledPermissionGuardTests
{
    private const string Refusal = "Only a Super User can grant or remove View user content.";

    private static readonly string[] WriteMethods =
    [
        nameof(IRoleRepository.CreateAsync), nameof(IRoleRepository.UpdateAsync), nameof(IRoleRepository.DeleteAsync),
        nameof(IRoleRepository.DeleteByIdAsync), nameof(IRoleRepository.SetControlledGrantsAsync),
        nameof(IRoleAssignmentRepository.ReplaceRoleAsync), nameof(IRoleAssignmentRepository.BulkReplaceRoleAsync),
    ];

    private static readonly RoleRecord ContentRole = new(
        "content-id", "Investigator", null, false,
        PermissionSet.Create(AdminPermissionCatalog.OperationalFailuresView, AdminPermissionCatalog.OperationalFailuresContentView), 1, null, "s");

    private static readonly RoleRecord PlainRole = new(
        "plain-id", "Moderator", null, false, PermissionSet.Create("admin.dashboard.view"), 1, null, "s");

    private static readonly RoleRecord SuperUserRole = new(
        "su-id", PrivilegedRoleNames.SuperUser, null, true, PermissionSet.Full, 1, null, "s");

    private static readonly RoleRecord UserRole = new(
        "user-id", DefaultRole.Name, null, true, PermissionSet.Empty, 1, null, "s");

    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly IRoleAssignmentRepository _assignmentRepository = Substitute.For<IRoleAssignmentRepository>();
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IAuthorizationCacheInvalidator _cacheInvalidator = Substitute.For<IAuthorizationCacheInvalidator>();
    private readonly ISender _mediator = Substitute.For<ISender>();

    public SuperUserControlledPermissionGuardTests()
    {
        _currentUser.UserId.Returns("actor-1");

        _roleRepository.GetByIdAsync("content-id", Arg.Any<CancellationToken>()).Returns(ContentRole);
        _roleRepository.GetByIdAsync("plain-id", Arg.Any<CancellationToken>()).Returns(PlainRole);
        _roleRepository.GetByIdAsync("su-id", Arg.Any<CancellationToken>()).Returns(SuperUserRole);
        _roleRepository.GetByIdAsync("user-id", Arg.Any<CancellationToken>()).Returns(UserRole);
        _roleRepository.GetByNormalizedNameAsync(DefaultRole.NormalizedName, Arg.Any<CancellationToken>()).Returns(UserRole);
        _roleRepository.ListByPermissionAsync(AdminPermissionCatalog.OperationalFailuresContentView, Arg.Any<CancellationToken>())
            .Returns([SuperUserRole, ContentRole]);
        _roleRepository.CreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<PermissionSet>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => ContentRole with { Id = "new-id", Name = ci.ArgAt<string>(0), Permissions = ci.ArgAt<PermissionSet>(2) });
        _roleRepository.UpdateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<PermissionSet>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => ContentRole with { Id = ci.ArgAt<string>(0), Name = ci.ArgAt<string>(1), Permissions = ci.ArgAt<PermissionSet>(3) });
        _roleRepository.DeleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        _roleRepository.DeleteByIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);

        _assignmentRepository.GetCurrentRoleIdAsync("plain-user", Arg.Any<CancellationToken>()).Returns("plain-id");
        _assignmentRepository.GetCurrentRoleIdAsync("holder-1", Arg.Any<CancellationToken>()).Returns("content-id");
        _assignmentRepository.ListUserIdsByRoleAsync("content-id", Arg.Any<CancellationToken>()).Returns(["holder-1"]);
        _assignmentRepository.ListUserIdsByRoleAsync("su-id", Arg.Any<CancellationToken>()).Returns(["super-1"]);
        _assignmentRepository.ReplaceRoleAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ReplaceRoleOutcome.Success);
        _assignmentRepository.BulkReplaceRoleAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BulkAssignResult(1, []));

        _identityService.GetRolesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        _identityService.CountActiveSuperUsersAsync(Arg.Any<CancellationToken>()).Returns(5);

        // ChangeUserRoleCommand delegates to AssignRoleCommand through the mediator — forward it to the real handler.
        var assignHandler = new AssignRoleCommandHandler(_roleRepository, _assignmentRepository, _identityService, _currentUser);
        _mediator.Send(Arg.Any<AssignRoleCommand>(), Arg.Any<CancellationToken>())
            .Returns(ci => assignHandler.Handle(ci.ArgAt<AssignRoleCommand>(0), CancellationToken.None));
    }

    public static TheoryData<string> GuardedPaths =>
    [
        "create with content.view",
        "update adding content.view",
        "update omitting a stored content.view",
        "delete a role holding it",
        "bulk-delete including one",
        "assign a role holding it",
        "replace a role holding it",
        "bulk-assign a role holding it",
        "bulk-assign over a user holding it",
        "change a holder's role through the legacy endpoint",
    ];

    [Theory]
    [MemberData(nameof(GuardedPaths))]
    public async Task Administrator_IsRefused_BeforeAnyWrite(string path)
    {
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(false);

        var act = () => RunAsync(path);

        var thrown = await act.Should().ThrowAsync<SuperUserRequiredException>();
        thrown.WithMessage(Refusal);
        thrown.Which.Should().BeAssignableTo<UnauthorizedAccessException>();
        WritesMade().Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(GuardedPaths))]
    public async Task SuperUser_Succeeds(string path)
    {
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(true);

        await RunAsync(path);

        WritesMade().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Administrator_EditingAHolderButEchoingTheStoredGrant_Succeeds()
    {
        // US1b scenario 3 — the picker keeps the disabled checkbox's state, so an unrelated edit echoes it back.
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(false);

        await UpdateHandler().Handle(
            new UpdateRoleCommand(
                "content-id", "Incident investigator", "Renamed",
                [AdminPermissionCatalog.OperationalFailuresView, AdminPermissionCatalog.OperationalFailuresContentView], "s"),
            CancellationToken.None);

        await _roleRepository.Received(1).UpdateAsync(
            "content-id", "Incident investigator", "Renamed", Arg.Any<PermissionSet>(), "s", "actor-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Administrator_ReassigningTheRoleAUserAlreadyHolds_ChangesNothing_AndSucceeds()
    {
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(false);

        await AssignHandler().Handle(new AssignRoleCommand("holder-1", "content-id", "content-id"), CancellationToken.None);

        await _assignmentRepository.Received(1).ReplaceRoleAsync("holder-1", "content-id", "content-id", "actor-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Administrator_ManagingRolesWithoutTheKey_IsUnaffected()
    {
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(false);

        await AssignHandler().Handle(new AssignRoleCommand("unassigned-user", "plain-id", null), CancellationToken.None);
        await new BulkDeleteRolesCommandHandler(_mediator, _roleRepository, _currentUser, _cacheInvalidator).Handle(
            new BulkDeleteRolesCommand(new BulkTarget(["plain-id"], false), null), CancellationToken.None);

        await _assignmentRepository.Received(1).ReplaceRoleAsync("unassigned-user", "plain-id", null, "actor-1", Arg.Any<CancellationToken>());
        await _roleRepository.Received(1).DeleteByIdAsync("plain-id", "actor-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Administrator_BulkAssigningOverASuperUser_IsLeftToThePrivilegedRoleRowSkip()
    {
        // The built-in roles are already Super-User-only (FR-016); BulkReplaceRoleAsync skips such a
        // row per user, as it always has, instead of this guard refusing the whole batch.
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(false);

        await new BulkAssignRoleCommandHandler(_mediator, _roleRepository, _assignmentRepository, _currentUser).Handle(
            new BulkAssignRoleCommand("plain-id", new BulkTarget(["super-1", "plain-user"], false), null), CancellationToken.None);

        await _assignmentRepository.Received(1).BulkReplaceRoleAsync(
            "plain-id", Arg.Any<IReadOnlyList<string>>(), "actor-1", Arg.Any<CancellationToken>());
    }

    private Task RunAsync(string path) => path switch
    {
        "create with content.view" => new CreateRoleCommandHandler(_roleRepository, _currentUser).Handle(
            new CreateRoleCommand(
                "Investigator 2", null,
                [AdminPermissionCatalog.OperationalFailuresView, AdminPermissionCatalog.OperationalFailuresContentView]),
            CancellationToken.None),
        "update adding content.view" => UpdateHandler().Handle(
            new UpdateRoleCommand(
                "plain-id", "Moderator", null,
                ["admin.dashboard.view", AdminPermissionCatalog.OperationalFailuresView, AdminPermissionCatalog.OperationalFailuresContentView], "s"),
            CancellationToken.None),
        "update omitting a stored content.view" => UpdateHandler().Handle(
            new UpdateRoleCommand("content-id", "Investigator", null, [AdminPermissionCatalog.OperationalFailuresView], "s"),
            CancellationToken.None),
        "delete a role holding it" => new DeleteRoleCommandHandler(_roleRepository, _currentUser, _cacheInvalidator).Handle(
            new DeleteRoleCommand("content-id", "s"), CancellationToken.None),
        "bulk-delete including one" => new BulkDeleteRolesCommandHandler(_mediator, _roleRepository, _currentUser, _cacheInvalidator).Handle(
            new BulkDeleteRolesCommand(new BulkTarget(["plain-id", "content-id"], false), null), CancellationToken.None),
        "assign a role holding it" => AssignHandler().Handle(
            new AssignRoleCommand("plain-user", "content-id", "plain-id"), CancellationToken.None),
        "replace a role holding it" => AssignHandler().Handle(
            new AssignRoleCommand("holder-1", "plain-id", "content-id"), CancellationToken.None),
        "bulk-assign a role holding it" => new BulkAssignRoleCommandHandler(_mediator, _roleRepository, _assignmentRepository, _currentUser).Handle(
            new BulkAssignRoleCommand("content-id", new BulkTarget(["plain-user"], false), null), CancellationToken.None),
        "bulk-assign over a user holding it" => new BulkAssignRoleCommandHandler(_mediator, _roleRepository, _assignmentRepository, _currentUser).Handle(
            new BulkAssignRoleCommand("plain-id", new BulkTarget(["plain-user", "holder-1"], false), null), CancellationToken.None),
        "change a holder's role through the legacy endpoint" => new ChangeUserRoleCommandHandler(
            _mediator, _roleRepository, _assignmentRepository, _identityService, _currentUser,
            new FakeLogger<ChangeUserRoleCommandHandler>()).Handle(
                new ChangeUserRoleCommand("holder-1", PrivilegedRoleNames.Regular), CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
    };

    private UpdateRoleCommandHandler UpdateHandler() =>
        new(_roleRepository, _assignmentRepository, _currentUser, _cacheInvalidator);

    private AssignRoleCommandHandler AssignHandler() =>
        new(_roleRepository, _assignmentRepository, _identityService, _currentUser);

    private List<string> WritesMade() =>
        _roleRepository.ReceivedCalls().Concat(_assignmentRepository.ReceivedCalls())
            .Select(call => call.GetMethodInfo().Name)
            .Where(WriteMethods.Contains)
            .ToList();
}
