using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AskLucy.WebAPI.Tests.Auth;

/// <summary>
/// Covers the OAuth challenge/link/complete endpoints that replaced the insecure
/// <c>POST /api/v1/auth/external</c> (T073 — see IdentityService.ResolveExternalLoginAsync's
/// doc comment for the vulnerability this closed). Google/Facebook are intentionally left
/// unconfigured in <see cref="CustomWebApplicationFactory"/> (no test client id/secret), so
/// full challenge→callback→complete round-trips against a real provider can't run here — only
/// the auth-gate and "provider not configured" boundaries are exercised, per the project's
/// established pattern for provider-dependent behavior (see tasks.md T028/T029).
/// </summary>
public sealed class ExternalLoginTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task ExternalChallenge_ShouldReturn400_WhenProviderIsUnknown()
    {
        var response = await _client.GetAsync("/api/v1/auth/external/twitter/challenge");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExternalChallenge_ShouldReturn400_WhenProviderIsNotConfigured()
    {
        // Google is a recognized provider name, but has no ClientId configured in the test
        // host, so Program.cs never registers its scheme — this is exactly the crash this
        // check (ResolveConfiguredSchemeAsync) prevents from surfacing as a raw 500.
        var response = await _client.GetAsync("/api/v1/auth/external/google/challenge");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExternalLink_ShouldReturn400_WhenProviderIsNotConfigured()
    {
        var response = await _client.GetAsync("/api/v1/auth/external/facebook/link?ticket=irrelevant");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task IssueExternalLoginLinkTicket_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync("/api/v1/auth/external/link-ticket", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IssueExternalLoginLinkTicket_ShouldReturnATicket_WhenAuthenticated()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await _client.PostAsync("/api/v1/auth/external/link-ticket", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ticket = await response.Content.ReadFromJsonAsync<string>();
        ticket.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CompleteExternalLogin_ShouldReturn401_WhenCodeIsInvalid()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/external/complete", new { code = "never-issued" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
