using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization.Roles.Commands.BulkDeleteRoles;
using AskLucy.Application.Authorization.Roles.Queries.GetRolesEligibleIds;
using AskLucy.Application.Common;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authorization;

public sealed class BulkDeleteRolesCommandHandlerTests
{
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IAuthorizationCacheInvalidator _cacheInvalidator = Substitute.For<IAuthorizationCacheInvalidator>();
    private readonly BulkDeleteRolesCommandHandler _handler;

    public BulkDeleteRolesCommandHandlerTests()
    {
        _currentUser.UserId.Returns("actor-1");
        _handler = new BulkDeleteRolesCommandHandler(_mediator, _roleRepository, _currentUser, _cacheInvalidator);
    }

    [Fact]
    public async Task Handle_DeletesEachResolvedRole_ReportsPerRoleUnassignedCount_EvictsHolders()
    {
        _roleRepository.DeleteByIdAsync("role-1", "actor-1", Arg.Any<CancellationToken>()).Returns(["user-1", "user-2"]);
        _roleRepository.DeleteByIdAsync("role-2", "actor-1", Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>?)[]);

        var result = await _handler.Handle(
            new BulkDeleteRolesCommand(new BulkTarget(["role-1", "role-2"], false), null), CancellationToken.None);

        result.Outcome.SucceededCount.Should().Be(2);
        result.Outcome.Skipped.Should().BeEmpty();
        result.UnassignedUserCounts["role-1"].Should().Be(2);
        result.UnassignedUserCounts["role-2"].Should().Be(0);
        _cacheInvalidator.Received(1).Evict("user-1");
        _cacheInvalidator.Received(1).Evict("user-2");
    }

    [Fact]
    public async Task Handle_SkipsNotFoundOrBuiltIn()
    {
        _roleRepository.DeleteByIdAsync("built-in-role", "actor-1", Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>?)null);

        var result = await _handler.Handle(
            new BulkDeleteRolesCommand(new BulkTarget(["built-in-role"], false), null), CancellationToken.None);

        result.Outcome.Skipped.Should().ContainSingle(s => s.Id == "built-in-role" && s.Reason == "NotFound");
    }

    [Fact]
    public async Task Handle_AllMatching_ReResolvesIdsAtExecutionTime()
    {
        _mediator.Send(Arg.Any<GetRolesEligibleIdsQuery>(), Arg.Any<CancellationToken>()).Returns(["role-9"]);
        _roleRepository.DeleteByIdAsync("role-9", "actor-1", Arg.Any<CancellationToken>()).Returns([]);

        var result = await _handler.Handle(
            new BulkDeleteRolesCommand(new BulkTarget(null, true), "custom"), CancellationToken.None);

        result.Outcome.SucceededCount.Should().Be(1);
        await _mediator.Received(1).Send(
            Arg.Is<GetRolesEligibleIdsQuery>(q => q != null && q.Search == "custom"), Arg.Any<CancellationToken>());
    }
}
