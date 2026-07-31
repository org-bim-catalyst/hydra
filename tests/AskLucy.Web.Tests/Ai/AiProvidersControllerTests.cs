using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Ai;

/// <summary>specs/005-multi-provider-ai-engine contracts/providers.md — user-facing catalog is authenticated but not role-gated (any signed-in user can read the enabled-provider/model catalog).</summary>
public sealed class AiProvidersControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("/api/v1/ai/providers")]
    [InlineData("/api/v1/ai/models")]
    [InlineData("/api/v1/ai/preferences")]
    public async Task ShouldReturn401_WhenAnonymous(string path)
    {
        var response = await _client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/v1/ai/providers")]
    [InlineData("/api/v1/ai/models")]
    [InlineData("/api/v1/ai/preferences")]
    public async Task ShouldPassAuthorization_WhenCallerIsAuthenticated_EvenWithoutAnAdminRole(string path)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await _client.GetAsync(path);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
