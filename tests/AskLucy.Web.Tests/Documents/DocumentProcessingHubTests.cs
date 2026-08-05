using System.Security.Claims;
using AskLucy.Infrastructure.Documents;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace AskLucy.Web.Tests.Documents;

/// <summary>
/// T065 — <see cref="DocumentProcessingHub"/> routes a connecting user into their own group
/// (server-assigned from the auth token, never client-supplied), and only additionally into
/// <see cref="DocumentProcessingHub.AdminDashboardGroup"/> for an administrator/super user
/// (FR-027, FR-045a). Directly sets <see cref="Hub.Context"/>/<see cref="Hub.Groups"/> — ASP.NET
/// Core SignalR hubs are designed to support this unit-test pattern without a live TestServer/
/// SignalR client connection.
/// </summary>
public sealed class DocumentProcessingHubTests
{
    private static HubCallerContext CreateContext(string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("connection-1");
        context.User.Returns(principal);
        return context;
    }

    [Fact]
    public async Task OnConnectedAsync_ShouldAddTheConnectionToItsOwnUserGroup_ForARegularUser()
    {
        var groups = Substitute.For<IGroupManager>();
        var hub = new DocumentProcessingHub { Context = CreateContext("user-1"), Groups = groups };

        await hub.OnConnectedAsync();

        await groups.Received(1).AddToGroupAsync("connection-1", DocumentProcessingHub.UserGroup("user-1"), Arg.Any<CancellationToken>());
        await groups.DidNotReceive().AddToGroupAsync("connection-1", DocumentProcessingHub.AdminDashboardGroup, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Super User")]
    public async Task OnConnectedAsync_ShouldAlsoJoinTheAdminDashboardGroup_ForAnAdministratorOrSuperUser(string role)
    {
        var groups = Substitute.For<IGroupManager>();
        var hub = new DocumentProcessingHub { Context = CreateContext("admin-1", role), Groups = groups };

        await hub.OnConnectedAsync();

        await groups.Received(1).AddToGroupAsync("connection-1", DocumentProcessingHub.UserGroup("admin-1"), Arg.Any<CancellationToken>());
        await groups.Received(1).AddToGroupAsync("connection-1", DocumentProcessingHub.AdminDashboardGroup, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnectedAsync_ShouldThrow_WhenNoUserIdClaimIsPresent()
    {
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("connection-1");
        context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity()));
        var hub = new DocumentProcessingHub { Context = context, Groups = Substitute.For<IGroupManager>() };

        var act = () => hub.OnConnectedAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
