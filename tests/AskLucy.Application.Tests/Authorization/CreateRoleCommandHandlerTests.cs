using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization.Roles.Commands.CreateRole;
using AskLucy.Domain.Authorization;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authorization;

public sealed class CreateRoleCommandHandlerTests
{
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly CreateRoleCommandHandler _handler;

    public CreateRoleCommandHandlerTests()
    {
        _currentUser.UserId.Returns("actor-1");
        _handler = new CreateRoleCommandHandler(_roleRepository, _currentUser);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesRoleAndReturnsDto()
    {
        var permissions = PermissionSet.Create("admin.mcp-servers.manage");
        _roleRepository.CreateAsync("Moderator", "desc", Arg.Any<PermissionSet>(), "actor-1", Arg.Any<CancellationToken>())
            .Returns(new RoleRecord("role-1", "Moderator", "desc", false, permissions, 0, null, "stamp-1"));

        var result = await _handler.Handle(new CreateRoleCommand("Moderator", "desc", ["admin.mcp-servers.manage"]), CancellationToken.None);

        result.Id.Should().Be("role-1");
        result.Name.Should().Be("Moderator");
        result.PermissionKeys.Should().Contain("admin.mcp-servers.view");
    }

    [Fact]
    public async Task Handle_DuplicateName_PropagatesDuplicateResourceException()
    {
        _roleRepository.CreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<PermissionSet>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<RoleRecord?>>(_ => throw new AskLucy.Domain.Common.DuplicateResourceException("A role named 'Moderator' already exists."));

        var act = () => _handler.Handle(new CreateRoleCommand("Moderator", null, ["admin.dashboard.view"]), CancellationToken.None);

        await act.Should().ThrowAsync<AskLucy.Domain.Common.DuplicateResourceException>();
    }

    [Fact]
    public async Task Handle_UnknownPermissionKey_ThrowsArgumentException()
    {
        var act = () => _handler.Handle(new CreateRoleCommand("Moderator", null, ["admin.nonexistent.view"]), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Handle_EmptyPermissionSet_ThrowsArgumentException()
    {
        var act = () => _handler.Handle(new CreateRoleCommand("Moderator", null, []), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Handle_ReservedName_ThrowsArgumentException()
    {
        var act = () => _handler.Handle(new CreateRoleCommand("Administrator", null, ["admin.dashboard.view"]), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
