using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Analytics;

/// <summary>
/// specs/023-flumeria-landing-experience, contracts/analytics-funnel-events-api.md. Burst/
/// rate-limit (429) behavior is intentionally verified manually via quickstart.md Scenario 7
/// rather than here — sending enough in-process requests to trip the fixed window would risk
/// polluting the shared rate-limit partition state for other tests using this same
/// <see cref="CustomWebApplicationFactory"/> instance (all anonymous calls share one IP
/// partition key), which no other controller test in this suite attempts either.
/// </summary>
public sealed class AnalyticsControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task RecordFunnelEvent_ShouldReturn202_ForAWellFormedAnonymousCtaClickedEvent()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/analytics/funnel-events", new
        {
            eventType = "CtaClicked",
            ctaId = "SignUp",
            sessionId = Guid.NewGuid(),
            occurredAtUtc = DateTime.UtcNow,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task RecordFunnelEvent_ShouldReturn202_ForAWellFormedAnonymousFunnelCompletedEvent()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/analytics/funnel-events", new
        {
            eventType = "FunnelCompleted",
            funnelType = "SignIn",
            sessionId = Guid.NewGuid(),
            occurredAtUtc = DateTime.UtcNow,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task RecordFunnelEvent_ShouldReturn400ProblemDetails_WhenCtaClickedIsMissingCtaId()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/analytics/funnel-events", new
        {
            eventType = "CtaClicked",
            sessionId = Guid.NewGuid(),
            occurredAtUtc = DateTime.UtcNow,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task RecordFunnelEvent_ShouldReturn400_WhenSessionIdIsEmpty()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/analytics/funnel-events", new
        {
            eventType = "CtaClicked",
            ctaId = "SignIn",
            sessionId = Guid.Empty,
            occurredAtUtc = DateTime.UtcNow,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
