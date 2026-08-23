using System.Net;
using System.Net.Http.Json;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Infrastructure.TheDigitalCore;
using AskLucy.Infrastructure.Tests.Ai;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.SiteAnalysis;

/// <summary>ADR-0009 — proves <see cref="TheDigitalCoreClient"/> builds correct requests against
/// TheDigitalCore's real <c>ProjectsApiController</c> shape, applies the name-then-geolocation
/// fallback (research.md Decision 8), and never silently swallows a relay failure (SC-007).</summary>
public sealed class TheDigitalCoreClientTests
{
    private static TheDigitalCoreClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder, out StubHttpMessageHandler handler, int maxRetries = 1)
    {
        handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://thedigitalcore.test/") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("TheDigitalCore").Returns(httpClient);

        var options = Options.Create(new TheDigitalCoreIntegrationOptions
        {
            BaseUrl = httpClient.BaseAddress.ToString(),
            ServiceAccountClientId = "test-client",
            ServiceAccountClientSecret = "test-secret",
            DefaultCompanyId = 1,
            DefaultProjectTypeId = 1,
            MaxRetries = maxRetries,
        });

        var authService = Substitute.For<ITheDigitalCoreAuthService>();
        authService.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("test-token");

        return new TheDigitalCoreClient(factory, options, authService);
    }

    [Fact]
    public async Task FindProjectAsync_ShouldReturnTheSingleNameMatch_WithoutSearchingByLocation()
    {
        var client = CreateClient(req =>
        {
            req.RequestUri!.ToString().Should().Contain("/api/projects/search").And.Contain("name=");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[]
                {
                    new { Id = 1, Name = "Al Safa Park", Number = (string?)null, Description = (string?)null, BucketKey = (string?)null, Latitude = 25.1m, Longitude = 55.2m, CompanyId = 1, CompanyName = "Acme", ProjectTypeId = 1, ProjectTypeName = "Park" },
                }),
            };
        }, out var handler);

        var result = await client.FindProjectAsync("Al Safa Park", 25.1m, 55.2m, CancellationToken.None);

        result.Should().ContainSingle(c => c.ProjectId == "1");
        handler.LastRequest!.RequestUri!.ToString().Should().Contain("name=");
    }

    [Fact]
    public async Task FindProjectAsync_ShouldFallBackToLocationSearch_WhenNameSearchIsInconclusive()
    {
        var callCount = 0;
        var client = CreateClient(req =>
        {
            callCount++;
            if (callCount == 1)
            {
                req.RequestUri!.ToString().Should().Contain("name=");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<object>()) };
            }

            req.RequestUri!.ToString().Should().Contain("latitude=").And.Contain("longitude=");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[]
                {
                    new { Id = 2, Name = "Al Safa Park", Number = (string?)null, Description = (string?)null, BucketKey = (string?)null, Latitude = 25.1m, Longitude = 55.2m, CompanyId = 1, CompanyName = "Acme", ProjectTypeId = 1, ProjectTypeName = "Park" },
                }),
            };
        }, out _);

        var result = await client.FindProjectAsync("Al Safa Park", 25.1m, 55.2m, CancellationToken.None);

        callCount.Should().Be(2);
        result.Should().ContainSingle(c => c.ProjectId == "2");
    }

    [Fact]
    public async Task CreateProjectAsync_ShouldReturnTheNewProjectId()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { Id = 42, Name = "Al Safa Park", Number = (string?)null, Description = (string?)null, BucketKey = (string?)null, Latitude = 25.1m, Longitude = 55.2m, CompanyId = 1, CompanyName = "Acme", ProjectTypeId = 1, ProjectTypeName = "Park" }),
        }, out _);

        var projectId = await client.CreateProjectAsync("Al Safa Park", 25.1m, 55.2m, CancellationToken.None);

        projectId.Should().Be("42");
    }

    [Fact]
    public async Task CreateProjectAsync_ShouldThrow_WhenTheDigitalCoreRejectsTheRequest()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest), out _);

        var act = () => client.CreateProjectAsync("Al Safa Park", null, null, CancellationToken.None);

        await act.Should().ThrowAsync<TheDigitalCoreIntegrationException>();
    }

    [Fact]
    public async Task RelayCategoryScoreResultAsync_ShouldThrow_NeverSilentlyDrop_AfterExhaustingRetries()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError), out _, maxRetries: 2);
        var result = new CategoryScoreResultDto("recreation", "Al Safa Park", 80m, [], [], false, null, Guid.NewGuid());

        var act = () => client.RelayCategoryScoreResultAsync("tdc-1", result, CancellationToken.None);

        await act.Should().ThrowAsync<TheDigitalCoreIntegrationException>();
    }

    [Fact]
    public async Task RelayCategoryScoreResultAsync_ShouldSucceed_WhenTheDigitalCoreAccepts()
    {
        var client = CreateClient(req =>
        {
            req.RequestUri!.ToString().Should().Contain("/category-scores");
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, out _);
        var result = new CategoryScoreResultDto("recreation", "Al Safa Park", 80m, [], [], false, null, Guid.NewGuid());

        var act = () => client.RelayCategoryScoreResultAsync("tdc-1", result, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
