using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AskLucy.Web.Tests.Auth;

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
        var response = await _client.GetAsync("/api/v1/auth/external/twitter/challenge", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExternalChallenge_ShouldReturn400_WhenProviderIsNotConfigured()
    {
        // Google is a recognized provider name, but has no ClientId configured in the test
        // host, so Program.cs never registers its scheme — this is exactly the crash this
        // check (ResolveConfiguredSchemeAsync) prevents from surfacing as a raw 500.
        var response = await _client.GetAsync("/api/v1/auth/external/google/challenge", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExternalLink_ShouldReturn400_WhenProviderIsNotConfigured()
    {
        var response = await _client.GetAsync("/api/v1/auth/external/facebook/link?ticket=irrelevant", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task IssueExternalLoginLinkTicket_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync("/api/v1/auth/external/link-ticket", content: null, cancellationToken: TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IssueExternalLoginLinkTicket_ShouldReturnATicket_WhenAuthenticated()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await _client.PostAsync("/api/v1/auth/external/link-ticket", content: null, cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ticket = await response.Content.ReadFromJsonAsync<string>(TestContext.Current.CancellationToken);
        ticket.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CompleteExternalLogin_ShouldReturn401_WhenCodeIsInvalid()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/external/complete", new { code = "never-issued" }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // specs/062-external-login-profile-sync T023: the default factory intentionally leaves
    // Google/Facebook unconfigured (see class doc comment) so the scheme is never registered —
    // here we spin up a second host with dummy credentials purely to inspect the configured
    // GoogleOptions/FacebookOptions, never to perform a real OAuth round trip.
    [Fact]
    public void ExternalLoginHandlers_ShouldMapNameAndPictureClaims_AndRequestGoogleProfileScope()
    {
        // Program.cs reads these directly off builder.Configuration BEFORE builder.Build() (to
        // decide whether to register the scheme at all), so values added via
        // ConfigureAppConfiguration — which only take effect at Build() time — arrive too late.
        // UseSetting writes into the ConfigurationManager immediately, which is visible to that
        // early read.
        using var configuredFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Authentication:Google:ClientId", "test-google-client-id");
            builder.UseSetting("Authentication:Google:ClientSecret", "test-google-client-secret");
            builder.UseSetting("Authentication:Facebook:AppId", "test-facebook-app-id");
            builder.UseSetting("Authentication:Facebook:AppSecret", "test-facebook-app-secret");
        });

        var googleOptions = configuredFactory.Services.GetRequiredService<IOptionsMonitor<GoogleOptions>>()
            .Get(GoogleDefaults.AuthenticationScheme);
        var facebookOptions = configuredFactory.Services.GetRequiredService<IOptionsMonitor<FacebookOptions>>()
            .Get(FacebookDefaults.AuthenticationScheme);

        googleOptions.Scope.Should().Contain("profile");
        googleOptions.ClaimActions.Select(a => a.ClaimType).Should().Contain(
            [ClaimTypes.GivenName, ClaimTypes.Surname, "urn:google:picture"]);

        facebookOptions.Fields.Should().Contain(["first_name", "last_name", "picture"]);
        facebookOptions.ClaimActions.Select(a => a.ClaimType).Should().Contain(
            [ClaimTypes.GivenName, ClaimTypes.Surname, "urn:facebook:picture"]);
    }
}
