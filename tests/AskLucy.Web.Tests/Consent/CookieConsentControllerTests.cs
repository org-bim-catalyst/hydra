using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Consent;

/// <summary>Same no-live-database pattern as <see cref="Users.LockUnlockUserTests"/>.</summary>
public sealed class CookieConsentControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetMine_ShouldReturn401_WhenNoBearerTokenIsProvided()
    {
        var response = await _client.GetAsync("/api/v1/users/me/cookie-consent");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMine_ShouldPassAuthorization_WhenAnAuthenticatedTokenIsProvided()
    {
        var token = TestJwtFactory.Create("user-1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/users/me/cookie-consent");

        // No live database in this environment (see CustomWebApplicationFactory) — proves
        // authorization let the caller through to the handler, never 401.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SaveMine_ShouldReturn401_WhenNoBearerTokenIsProvided()
    {
        var response = await _client.PutAsJsonAsync("/api/v1/users/me/cookie-consent", new { functional = true, analytics = true, marketing = true });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SaveMine_ShouldReturn400ProblemDetails_WhenARequiredBooleanFieldIsOmitted()
    {
        var token = TestJwtFactory.Create("user-1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // "analytics" omitted entirely — FluentValidation's NotNull() must reject this before
        // the handler (and the database) is ever reached.
        var response = await _client.PutAsJsonAsync("/api/v1/users/me/cookie-consent", new { functional = true, marketing = true });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}
