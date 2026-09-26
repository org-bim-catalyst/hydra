using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization.Assignments.Commands.BulkAssignRole;
using AskLucy.Application.Authorization.Assignments.Queries.GetRoleAssignmentsEligibleIds;
using AskLucy.Application.Common;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authorization;

public sealed class BulkAssignRoleCommandHandlerTests
{
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly IRoleAssignmentRepository _roleAssignmentRepository = Substitute.For<IRoleAssignmentRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly BulkAssignRoleCommandHandler _handler;

    public BulkAssignRoleCommandHandlerTests()
    {
        _currentUser.UserId.Returns("actor-1");
        _roleRepository.ListByPermissionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        _handler = new BulkAssignRoleCommandHandler(_mediator, _roleRepository, _roleAssignmentRepository, _currentUser);
    }

    [Fact]
    public async Task Handle_ExplicitIds_DelegatesDirectlyToBulkReplaceRoleAsync()
    {
        var expectedIds = new[] { "user-1", "user-2" };
        _roleAssignmentRepository.BulkReplaceRoleAsync("role-1", Arg.Is<IReadOnlyList<string>>(ids => ids != null && ids.SequenceEqual(expectedIds)), "actor-1", Arg.Any<CancellationToken>())
            .Returns(new BulkAssignResult(2, []));

        var outcome = await _handler.Handle(
            new BulkAssignRoleCommand("role-1", new BulkTarget(["user-1", "user-2"], false), null), CancellationToken.None);

        outcome.SucceededCount.Should().Be(2);
        outcome.Skipped.Should().BeEmpty();
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<GetRoleAssignmentsEligibleIdsQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AllMatching_ReResolvesIdsAtExecutionTime()
    {
        _mediator.Send(Arg.Is<GetRoleAssignmentsEligibleIdsQuery>(q => q != null && q.RoleId == "role-1" && q.Search == "ana"), Arg.Any<CancellationToken>())
            .Returns(["user-9"]);
        var expectedIds = new[] { "user-9" };
        _roleAssignmentRepository.BulkReplaceRoleAsync("role-1", Arg.Is<IReadOnlyList<string>>(ids => ids != null && ids.SequenceEqual(expectedIds)), "actor-1", Arg.Any<CancellationToken>())
            .Returns(new BulkAssignResult(1, []));

        var outcome = await _handler.Handle(
            new BulkAssignRoleCommand("role-1", new BulkTarget(null, true), "ana"), CancellationToken.None);

        outcome.SucceededCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_MapsSkipReasonsToStrings()
    {
        _roleAssignmentRepository.BulkReplaceRoleAsync("role-1", Arg.Any<IReadOnlyList<string>>(), "actor-1", Arg.Any<CancellationToken>())
            .Returns(new BulkAssignResult(0, [("user-1", BulkAssignSkipReason.UserLocked), ("user-2", BulkAssignSkipReason.AlreadyAssigned)]));

        var outcome = await _handler.Handle(
            new BulkAssignRoleCommand("role-1", new BulkTarget(["user-1", "user-2"], false), null), CancellationToken.None);

        outcome.Skipped.Should().Contain(s => s.Id == "user-1" && s.Reason == "UserLocked");
        outcome.Skipped.Should().Contain(s => s.Id == "user-2" && s.Reason == "AlreadyAssigned");
    }
}
