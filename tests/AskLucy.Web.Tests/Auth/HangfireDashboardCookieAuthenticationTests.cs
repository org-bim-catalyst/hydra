using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using AskLucy.Application.Admin.Commands.IssueHangfireDashboardSession;
using AskLucy.Web.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AskLucy.Web.Tests.Auth;

/// <summary>
/// specs/060-hangfire-dashboard-access — proves the `/hangfire`-scoped cookie bridge in
/// Program.cs's `OnMessageReceived`/`OnTokenValidated` (research.md Decisions 1 &amp; 2), not
/// just the minting endpoint tested by <see cref="Controllers.AdminHangfireSessionEndpointTests"/>.
/// </summary>
public sealed class HangfireDashboardCookieAuthenticationTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory = factory;

    [Fact]
    public async Task GetHangfire_ShouldAuthenticate_WhenTheDashboardCookieCarriesAValidPurposeClaim()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwtFactory.Create("admin-1", ["Administrator"], [], [new Claim(HangfireDashboardSessionClaims.PurposeClaimType, HangfireDashboardSessionClaims.PurposeClaimValue)]));

        var sessionResponse = await client.PostAsync("/api/v1/admin/hangfire/session", content: null, TestContext.Current.CancellationToken);
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        client.DefaultRequestHeaders.Authorization = null;
        var dashboardResponse = await client.GetAsync("/hangfire", TestContext.Current.CancellationToken);

        dashboardResponse.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        dashboardResponse.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetHangfire_ShouldNotAuthenticate_WhenNoCookieIsPresent()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/hangfire", TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetHangfire_ShouldRejectTheCookie_WhenItsTokenLacksTheDashboardPurposeClaim()
    {
        // A validly signed access token minted for a different purpose (e.g. a stolen/replayed
        // normal session token) must not authenticate `/hangfire` — research.md Decision 2.
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        var ordinaryToken = TestJwtFactory.Create("admin-1", "Administrator");
        client.DefaultRequestHeaders.Add("Cookie", $"{HangfireDashboardCookie.Name}={ordinaryToken}");

        var response = await client.GetAsync("/hangfire", TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
