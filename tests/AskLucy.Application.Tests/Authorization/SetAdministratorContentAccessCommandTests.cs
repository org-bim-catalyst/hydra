using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization;
using AskLucy.Application.Authorization.Roles.Commands.SetAdministratorContentAccess;
using AskLucy.Application.Authorization.Roles.Queries.GetAdministratorContentAccess;
using AskLucy.Application.Users;
using AskLucy.Domain.Authorization;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authorization;

/// <summary>
/// specs/074 T079 (FR-016g, FR-016k) — only a Super User may let Administrators view user content.
/// The grant is stored on the built-in Administrator role through
/// <see cref="IRoleRepository.SetControlledGrantsAsync"/> (which writes the <c>RoleUpdated</c> audit
/// row — covered by its persistence test), every Administrator's cached permissions are evicted, and
/// the resolver sees the change on the next request.
/// </summary>
public sealed class SetAdministratorContentAccessCommandTests
{
    private const string ContentView = AdminPermissionCatalog.OperationalFailuresContentView;

    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly IRoleAssignmentRepository _assignmentRepository = Substitute.For<IRoleAssignmentRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IAuthorizationCacheInvalidator _cacheInvalidator = Substitute.For<IAuthorizationCacheInvalidator>();
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly SetAdministratorContentAccessCommandHandler _handler;

    // What the Administrator role row stores for the Super-User-controlled keys — the stand-in database.
    private List<string> _storedControlledKeys = [];

    public SetAdministratorContentAccessCommandTests()
    {
        _currentUser.UserId.Returns("super-1");
        _roleRepository.GetByNormalizedNameAsync("ADMINISTRATOR", Arg.Any<CancellationToken>()).Returns(_ => AdministratorRole());
        _roleRepository.SetControlledGrantsAsync("admin-id", Arg.Any<IReadOnlyCollection<string>>(), "super-1", Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                _storedControlledKeys = [.. ci.ArgAt<IReadOnlyCollection<string>>(1)];
                return AdministratorRole();
            });
        _assignmentRepository.ListUserIdsByRoleAsync("admin-id", Arg.Any<CancellationToken>()).Returns(["admin-a", "admin-b"]);
        _identityService.GetRolesAsync("admin-a", Arg.Any<CancellationToken>()).Returns([PrivilegedRoleNames.Administrator]);

        _handler = new SetAdministratorContentAccessCommandHandler(_roleRepository, _assignmentRepository, _currentUser, _cacheInvalidator);
    }

    [Fact]
    public async Task Administrator_IsRefused_AndNothingIsStored()
    {
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(false);

        var act = () => _handler.Handle(new SetAdministratorContentAccessCommand(true), CancellationToken.None);

        await act.Should().ThrowAsync<SuperUserRequiredException>()
            .WithMessage("Only a Super User can grant or remove View user content.");
        await _roleRepository.DidNotReceiveWithAnyArgs().SetControlledGrantsAsync(default!, default!, default!, TestContext.Current.CancellationToken);
        _cacheInvalidator.DidNotReceiveWithAnyArgs().Evict(default!);
    }

    [Fact]
    public async Task SuperUser_Granting_StoresTheKey_AndEvictsEveryAdministrator()
    {
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(true);

        var result = await _handler.Handle(new SetAdministratorContentAccessCommand(true), CancellationToken.None);

        result.Granted.Should().BeTrue();
        _storedControlledKeys.Should().Equal(ContentView);
        await _roleRepository.Received(1).SetControlledGrantsAsync(
            "admin-id", Arg.Any<IReadOnlyCollection<string>>(), "super-1", Arg.Any<CancellationToken>());
        _cacheInvalidator.Received(1).Evict("admin-a");
        _cacheInvalidator.Received(1).Evict("admin-b");
    }

    [Fact]
    public async Task SuperUser_Revoking_RemovesTheKey()
    {
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(true);
        _storedControlledKeys = [ContentView];

        var result = await _handler.Handle(new SetAdministratorContentAccessCommand(false), CancellationToken.None);

        result.Granted.Should().BeFalse();
        _storedControlledKeys.Should().BeEmpty();
        _cacheInvalidator.Received(1).Evict("admin-a");
    }

    [Fact]
    public async Task TheResolverAndTheSwitch_ReflectEachChange()
    {
        _currentUser.IsInRole(PrivilegedRoleNames.SuperUser).Returns(true);
        var resolver = new EffectivePermissionResolver(_identityService, _roleRepository);
        var readSwitch = new GetAdministratorContentAccessQueryHandler(_roleRepository);
        var ct = TestContext.Current.CancellationToken;

        (await resolver.ResolveAsync("admin-a", ct)).Contains(ContentView).Should().BeFalse();

        await _handler.Handle(new SetAdministratorContentAccessCommand(true), ct);
        (await resolver.ResolveAsync("admin-a", ct)).Contains(ContentView).Should().BeTrue();
        (await readSwitch.Handle(new GetAdministratorContentAccessQuery(), ct)).Granted.Should().BeTrue();

        await _handler.Handle(new SetAdministratorContentAccessCommand(false), ct);
        var afterRevoke = await resolver.ResolveAsync("admin-a", ct);
        afterRevoke.Contains(ContentView).Should().BeFalse();
        afterRevoke.Contains(AdminPermissionCatalog.OperationalFailuresView).Should().BeTrue("every other catalogue key stays automatic");
        (await readSwitch.Handle(new GetAdministratorContentAccessQuery(), ct)).Granted.Should().BeFalse();
    }

    private RoleRecord AdministratorRole() => new(
        "admin-id", PrivilegedRoleNames.Administrator, null, true,
        BuiltInRolePermissions.Administrator(_storedControlledKeys), 2, null, "s");
}
