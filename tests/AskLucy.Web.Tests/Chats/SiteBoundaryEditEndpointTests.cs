using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.Chats;

/// <summary>
/// specs/079 - the outer gate and the published contract of <c>PUT /api/v1/chats/{id}/site-boundary</c>.
/// The cross-user 404, the 409 with <c>currentRevision</c>, and the 422 with <c>ringIndex</c> and
/// <c>reason</c> are asserted where they can run without a second real authenticated user:
/// <c>SaveSiteBoundaryEditCommandTests</c> (handler) and <c>ProblemDetailsMiddlewareTests</c> (mapping),
/// the same split <see cref="OwnershipTests"/> documents. One derived factory per class, not a host
/// per test (memory: WithWebHostBuilder stalls Web.Tests).
/// </summary>
public sealed class SiteBoundaryEditEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Put_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PutAsync(
            $"/api/v1/chats/{Guid.NewGuid()}/site-boundary",
            JsonContent.Create(new { expectedRevision = Guid.NewGuid().ToString(), rings = Array.Empty<object>() }),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostCombine_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync(
            $"/api/v1/chats/{Guid.NewGuid()}/site-boundary/actions/combine",
            JsonContent.Create(new { rings = Array.Empty<object>(), operation = "Add", centre = new { latitude = 1, longitude = 1 }, radiusMeters = 10 }),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostReset_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync(
            $"/api/v1/chats/{Guid.NewGuid()}/site-boundary/actions/reset",
            JsonContent.Create(new { expectedRevision = Guid.NewGuid().ToString() }),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OpenApiDocument_ShouldListTheResetEndpoint_WithItsErrorResponses()
    {
        var provider = factory.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");

        var document = await provider.GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);

        var post = document.Paths["/api/v1/chats/{id}/site-boundary/actions/reset"].Operations!.Single(o => o.Key == System.Net.Http.HttpMethod.Post).Value;
        post.Responses!.Keys.Should().Contain(["200", "400", "404", "409"]);
    }

    [Fact]
    public async Task OpenApiDocument_ShouldListTheCombineEndpoint_WithItsErrorResponses()
    {
        var provider = factory.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");

        var document = await provider.GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);

        document.Paths.Keys.Should().Contain("/api/v1/chats/{id}/site-boundary/actions/combine");
        var post = document.Paths["/api/v1/chats/{id}/site-boundary/actions/combine"].Operations!.Single(o => o.Key == System.Net.Http.HttpMethod.Post).Value;
        post.Responses!.Keys.Should().Contain(["200", "400", "404", "422"]);
    }

    [Fact]
    public async Task OpenApiDocument_ShouldListTheSiteBoundaryEditEndpoint_WithItsErrorResponses()
    {
        var provider = factory.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");

        var document = await provider.GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);

        document.Paths.Keys.Should().Contain("/api/v1/chats/{id}/site-boundary");
        var put = document.Paths["/api/v1/chats/{id}/site-boundary"].Operations!.Single(o => o.Key == System.Net.Http.HttpMethod.Put).Value;
        put.Responses!.Keys.Should().Contain(["200", "400", "404", "409", "422"]);
    }
}
