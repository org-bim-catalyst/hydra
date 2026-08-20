using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Routing;

/// <summary>
/// specs/029-fix-chat-widget-bugs T002/research.md Decision 7 — regression test for the
/// production bug where the SPA-fallback middleware's hand-rolled prefix-exclusion list
/// (only "/api", "/openapi", "/health") silently swallowed every GET to a SignalR hub path
/// as the SPA's index.html, because "/hubs" was never added to it. Confirmed in production
/// console logs as `EventSource... MIME type ("text/html")` / WebSocket handshake failures
/// for /hubs/panels — this test asserts the same failure mode for all 6 mapped hubs, not
/// just the one first observed, per FR-011's "apply uniformly" requirement.
/// </summary>
public sealed class HubFallbackRoutingTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    public static IEnumerable<object[]> HubPaths =>
    [
        ["/hubs/document-processing"],
        ["/hubs/retrieval-indexing"],
        ["/hubs/memory"],
        ["/hubs/agent-execution"],
        ["/hubs/workflow-execution"],
        ["/hubs/panels"],
    ];

    [Theory]
    [MemberData(nameof(HubPaths))]
    public async Task GetHubPath_ShouldNotBeServedAsTheSpaShell(string hubPath)
    {
        var response = await _client.GetAsync(hubPath, TestContext.Current.CancellationToken);

        // The exact bug symptom: the SPA-fallback middleware serving index.html for a hub
        // path it should never have intercepted. SignalR's own handling of a bare GET
        // without a negotiated connection (400/404/whatever it decides) is acceptable here —
        // the only thing this test rules out is the request being silently reinterpreted as
        // a client-side route.
        response.Content.Headers.ContentType?.MediaType.Should().NotBe(MediaTypeHeaderValue.Parse("text/html").MediaType);
    }

    [Fact]
    public async Task GetUnknownSpaRoute_ShouldStillBeServedAsTheSpaShell()
    {
        // The fix must not throw the baby out with the bathwater: an ordinary client-side
        // route with no matching endpoint (controller, hub, health check, OpenAPI, static
        // file) must still fall through to index.html via app.MapFallback.
        var response = await _client.GetAsync("/some/client-side/route", TestContext.Current.CancellationToken);

        response.Content.Headers.ContentType?.MediaType.Should().Be(MediaTypeHeaderValue.Parse("text/html").MediaType);
    }
}
