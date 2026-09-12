using System.Text.Json;
using AskLucy.Application.Panels;
using AskLucy.Infrastructure.Panels;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Panels;

/// <summary>T023 — <see cref="PanelNotifier"/> targets only the triggering user's SignalR group
/// (<see cref="PanelHub.UserGroup"/>), never a broadcast (spec 028 FR-023, contracts/panel-hub-events.md).</summary>
public sealed class PanelNotifierTests
{
    private readonly IHubClients _hubClients = Substitute.For<IHubClients>();
    private readonly IClientProxy _ownerGroupProxy = Substitute.For<IClientProxy>();

    private PanelNotifier CreateSut(string userId)
    {
        _hubClients.Group(PanelHub.UserGroup(userId)).Returns(_ownerGroupProxy);
        var hubContext = Substitute.For<IHubContext<PanelHub>>();
        hubContext.Clients.Returns(_hubClients);
        return new PanelNotifier(hubContext);
    }

    [Fact]
    public async Task PanelRequestedAsync_ShouldSendOnlyToTheTriggeringUsersGroup()
    {
        var sut = CreateSut("user-1");
        var content = JsonSerializer.SerializeToElement(new { version = 1, blocks = Array.Empty<object>() });
        var request = PanelRequestDto.ForContent("req-1", "Sun Exposure", content);

        await sut.PanelRequestedAsync("user-1", request, CancellationToken.None);

        await _ownerGroupProxy.Received(1).SendCoreAsync("PanelRequested", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PanelRequestedAsync_ShouldNeverSendToAnotherUsersGroup()
    {
        var sut = CreateSut("user-2");
        var otherUserProxy = Substitute.For<IClientProxy>();
        _hubClients.Group(PanelHub.UserGroup("user-3")).Returns(otherUserProxy);
        var data = JsonSerializer.SerializeToElement(new { });
        var request = PanelRequestDto.ForLive("req-2", "Data", "some-live-panel-kind", data);

        await sut.PanelRequestedAsync("user-2", request, CancellationToken.None);

        await otherUserProxy.DidNotReceive().SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }
}
