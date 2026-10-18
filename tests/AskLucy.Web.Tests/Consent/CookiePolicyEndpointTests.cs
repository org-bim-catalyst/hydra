using System.Net;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Consent;

[Collection(DatabaseSerialGroup.Name)]
public sealed class CookiePolicyEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetPolicy_ShouldReturn200_WithNoAuthorizationHeader()
    {
        var response = await _client.GetAsync("/api/v1/cookie-policy", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
