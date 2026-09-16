using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization.Assignments.Queries.ListRoleAssignments;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authorization;

public sealed class ListRoleAssignmentsQueryHandlerTests
{
    private readonly IRoleAssignmentRepository _assignmentRepository = Substitute.For<IRoleAssignmentRepository>();
    private readonly ListRoleAssignmentsQueryHandler _handler;

    public ListRoleAssignmentsQueryHandlerTests()
    {
        _handler = new ListRoleAssignmentsQueryHandler(_assignmentRepository);
    }

    [Fact]
    public async Task Handle_PassesSearchAndRoleIdThrough()
    {
        _assignmentRepository.SearchAsync("ana", "role-1", false, false, 1, 20, Arg.Any<CancellationToken>())
            .Returns((new List<RoleAssignmentRecord>(), 0));

        await _handler.Handle(new ListRoleAssignmentsQuery("ana", "role-1", false), CancellationToken.None);

        await _assignmentRepository.Received(1).SearchAsync("ana", "role-1", false, false, 1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoneSentinel_TranslatesToNoRoleFilter()
    {
        _assignmentRepository.SearchAsync(null, null, true, false, 1, 20, Arg.Any<CancellationToken>())
            .Returns((new List<RoleAssignmentRecord>(), 0));

        await _handler.Handle(new ListRoleAssignmentsQuery(null, "none", false), CancellationToken.None);

        await _assignmentRepository.Received(1).SearchAsync(null, null, true, false, 1, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MapsRecordsToDto_RoleNullForNoRoleHolder()
    {
        var records = new List<RoleAssignmentRecord>
        {
            new("user-1", "a@b.com", "Ana", "Lee", false, "role-1", "Moderator", false),
            new("user-2", "c@d.com", null, null, true, null, null, false),
        };
        _assignmentRepository.SearchAsync(null, null, false, false, 1, 20, Arg.Any<CancellationToken>()).Returns((records, 2));

        var result = await _handler.Handle(new ListRoleAssignmentsQuery(null, null, false), CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.Items[0].Role!.Name.Should().Be("Moderator");
        result.Items[1].Role.Should().BeNull();
        result.Items[1].IsLockedOut.Should().BeTrue();
    }
}
