using System.Net;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Consent;

public sealed class CookiePolicyEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetPolicy_ShouldReturn200_WithNoAuthorizationHeader()
    {
        var response = await _client.GetAsync("/api/v1/cookie-policy");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
