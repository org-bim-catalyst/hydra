using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization.Assignments.Queries.GetRoleAssignmentsEligibleIds;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authorization;

public sealed class GetRoleAssignmentsEligibleIdsQueryHandlerTests
{
    private readonly IRoleAssignmentRepository _roleAssignmentRepository = Substitute.For<IRoleAssignmentRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly GetRoleAssignmentsEligibleIdsQueryHandler _handler;

    public GetRoleAssignmentsEligibleIdsQueryHandlerTests()
    {
        _handler = new GetRoleAssignmentsEligibleIdsQueryHandler(_roleAssignmentRepository, _currentUser);
    }

    [Fact]
    public async Task Handle_NonSuperUserActor_PassesActorIsSuperUserFalse()
    {
        _currentUser.IsInRole("Super User").Returns(false);
        _roleAssignmentRepository.ListEligibleIdsAsync("role-1", "ana", false, Arg.Any<CancellationToken>())
            .Returns(["user-1"]);

        var result = await _handler.Handle(new GetRoleAssignmentsEligibleIdsQuery("role-1", "ana"), CancellationToken.None);

        result.Should().Equal("user-1");
        await _roleAssignmentRepository.Received(1).ListEligibleIdsAsync("role-1", "ana", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SuperUserActor_PassesActorIsSuperUserTrue()
    {
        _currentUser.IsInRole("Super User").Returns(true);
        _roleAssignmentRepository.ListEligibleIdsAsync("role-1", null, true, Arg.Any<CancellationToken>())
            .Returns(["user-1", "user-2"]);

        var result = await _handler.Handle(new GetRoleAssignmentsEligibleIdsQuery("role-1", null), CancellationToken.None);

        result.Should().Equal("user-1", "user-2");
    }
}
