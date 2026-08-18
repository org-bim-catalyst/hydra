using System.Security.Claims;
using AskLucy.Infrastructure.Agents;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Xunit;

namespace AskLucy.Web.Tests.Agents;

/// <summary>
/// spec.md User Story 4, contracts/agent-execution-events.md — <see cref="AgentExecutionHub"/>
/// routes a connecting user into their own server-assigned group (never client-supplied), never a
/// per-execution group (one connection carries every one of the caller's executions' events).
/// Mirrors <c>DocumentProcessingHubTests</c>' pattern exactly — directly sets <see cref="Hub.Context"/>/
/// <see cref="Hub.Groups"/>, no live TestServer/SignalR client connection needed.
/// </summary>
public sealed class AgentExecutionHubTests
{
    private static HubCallerContext CreateContext(string? userId)
    {
        var claims = userId is null ? [] : new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("connection-1");
        context.User.Returns(principal);
        return context;
    }

    [Fact]
    public async Task OnConnectedAsync_ShouldAddTheConnectionToItsOwnUserGroup()
    {
        var groups = Substitute.For<IGroupManager>();
        var hub = new AgentExecutionHub { Context = CreateContext("user-1"), Groups = groups };

        await hub.OnConnectedAsync();

        await groups.Received(1).AddToGroupAsync("connection-1", AgentExecutionHub.UserGroup("user-1"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnectedAsync_ShouldThrow_WhenNoUserIdClaimIsPresent()
    {
        var hub = new AgentExecutionHub { Context = CreateContext(null), Groups = Substitute.For<IGroupManager>() };

        var act = () => hub.OnConnectedAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
