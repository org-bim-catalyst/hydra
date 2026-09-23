using System.Net;
using System.Security.Claims;
using AskLucy.Application.Authorization;
using AskLucy.Infrastructure.CustomModels;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using Xunit;

namespace AskLucy.Web.Tests.CustomModels;

/// <summary>
/// specs/072 T047 — who may join the deployments hub. The permission claims are the ones the claims
/// transformation attaches for any role (built-in or custom), so each case here is just the
/// principal that transformation would have produced.
/// </summary>
public sealed class CustomModelDeploymentHubTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly IGroupManager _groups = Substitute.For<IGroupManager>();
    private readonly FakeLogger<CustomModelDeploymentHub> _logger = new();

    [Fact]
    public async Task OnConnected_WithTheViewPermission_JoinsTheViewersGroup()
    {
        var hub = CreateHub(Principal("admin-1", "Administrator", "admin.custom-models.view"));

        await hub.OnConnectedAsync();

        await _groups.Received(1).AddToGroupAsync("connection-1", CustomModelDeploymentHub.ViewersGroup, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnected_CustomRoleHoldingThePermission_JoinsToo()
    {
        var hub = CreateHub(Principal("ops-1", "Model Operators", "admin.custom-models.view"));

        await hub.OnConnectedAsync();

        await _groups.Received(1).AddToGroupAsync("connection-1", CustomModelDeploymentHub.ViewersGroup, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnected_WithOnlyAiProviderPermissions_IsRefused_AndLogged()
    {
        var hub = CreateHub(Principal("admin-2", "Provider Admins", "admin.ai-providers.view", "admin.ai-providers.manage"));

        var act = () => hub.OnConnectedAsync();

        await act.Should().ThrowAsync<HubException>().WithMessage("Forbidden");
        await _groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!, default);
        _logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Message.Contains("admin-2"));
    }

    [Fact]
    public async Task OnConnected_WithNoPrincipal_IsRefused()
    {
        var hub = CreateHub(new ClaimsPrincipal(new ClaimsIdentity()));

        var act = () => hub.OnConnectedAsync();

        await act.Should().ThrowAsync<HubException>();
        await _groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!, default);
    }

    [Fact]
    public void Hub_RequiresAnAuthenticatedCaller()
    {
        typeof(CustomModelDeploymentHub).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Negotiate_Anonymous_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/hubs/custom-model-deployments/negotiate?negotiateVersion=1", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static ClaimsPrincipal Principal(string userId, string role, params string[] permissions)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId), new(ClaimTypes.Role, role) };
        claims.AddRange(permissions.Select(p => new Claim(PermissionClaims.Type, p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private CustomModelDeploymentHub CreateHub(ClaimsPrincipal user)
    {
        var context = Substitute.For<HubCallerContext>();
        context.User.Returns(user);
        context.ConnectionId.Returns("connection-1");

        return new CustomModelDeploymentHub(_logger) { Context = context, Groups = _groups };
    }
}
