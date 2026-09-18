using System.Net;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.SiteAnalysis;

/// <summary>
/// contracts/site-analysis-api.md FR-019: a site analysis is visible only to the user who
/// initiated it. Mirrors <c>AskLucy.Web.Tests.Workflows.WorkflowsControllerTests</c>'s scope
/// boundary for this environment exactly: the cross-user 404-not-403 denial logic is unit-tested
/// at the Application layer (<c>GetSiteAnalysisQueryHandlerTests</c>), where it can be verified
/// without a live database and a second real authenticated user. This class covers the outer auth
/// gate (unauthenticated request → 401), which is what's runnable in this environment.
/// </summary>
public sealed class SiteAnalysesControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetSiteAnalysis_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/site-analyses/{Guid.NewGuid()}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListSiteAnalysesByChat_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/site-analyses?userChatId={Guid.NewGuid()}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
