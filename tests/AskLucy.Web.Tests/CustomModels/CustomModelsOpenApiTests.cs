using FluentAssertions;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.CustomModels;

/// <summary>
/// specs/072 T089 — the OpenAPI document still generates with the new controller in it.
/// <c>/openapi</c> is only mapped in Development, so this asks the registered document provider
/// directly rather than going over HTTP.
/// </summary>
public sealed class CustomModelsOpenApiTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task OpenApiDocument_ShouldGenerate_AndListTheCustomModelEndpoints()
    {
        var provider = factory.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");

        var document = await provider.GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);

        document.Paths.Keys.Should().Contain(
        [
            "/api/v1/admin/custom-models",
            "/api/v1/admin/custom-models/{id}",
            "/api/v1/admin/custom-models/deployment-status",
            "/api/v1/admin/custom-models/source-preview",
            "/api/v1/admin/custom-models/{id}/actions/cancel",
            "/api/v1/admin/custom-models/{id}/availability",
        ]);
    }
}
