using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AskLucy.Domain.Ai;
using AskLucy.Web.Contracts;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Ai;

/// <summary>
/// specs/005-multi-provider-ai-engine User Story 1 (FR-001–FR-004). Same no-live-database,
/// self-signed-JWT pattern as <see cref="Admin.AdminDashboardTests"/> — role evaluation is
/// purely a function of the token's claims, independent of any data access.
/// </summary>
public sealed class AdminAiProvidersControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly Guid SomeProviderId = Guid.NewGuid();

    [Theory]
    [InlineData("GET", "/api/v1/admin/ai/providers")]
    [InlineData("PATCH", "/api/v1/admin/ai/providers/{id}")]
    [InlineData("PUT", "/api/v1/admin/ai/providers/{id}/credential")]
    [InlineData("DELETE", "/api/v1/admin/ai/providers/{id}/credential")]
    [InlineData("GET", "/api/v1/admin/ai/providers/{id}/models")]
    [InlineData("PATCH", "/api/v1/admin/ai/models/{id}")]
    [InlineData("POST", "/api/v1/admin/ai/providers/{id}/models/actions/sync")]
    [InlineData("POST", "/api/v1/admin/ai/providers/{id}/models/actions/sync/apply")]
    public async Task ShouldReturn401_WhenAnonymous(string method, string pathTemplate)
    {
        var response = await SendAsync(method, pathTemplate);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("GET", "/api/v1/admin/ai/providers")]
    [InlineData("PATCH", "/api/v1/admin/ai/providers/{id}")]
    [InlineData("PUT", "/api/v1/admin/ai/providers/{id}/credential")]
    [InlineData("DELETE", "/api/v1/admin/ai/providers/{id}/credential")]
    [InlineData("GET", "/api/v1/admin/ai/providers/{id}/models")]
    [InlineData("PATCH", "/api/v1/admin/ai/models/{id}")]
    [InlineData("POST", "/api/v1/admin/ai/providers/{id}/models/actions/sync")]
    [InlineData("POST", "/api/v1/admin/ai/providers/{id}/models/actions/sync/apply")]
    public async Task ShouldReturn403_WhenCallerHasNoAdminRole(string method, string pathTemplate)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await SendAsync(method, pathTemplate);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("GET", "/api/v1/admin/ai/providers")]
    [InlineData("PATCH", "/api/v1/admin/ai/providers/{id}")]
    [InlineData("PUT", "/api/v1/admin/ai/providers/{id}/credential")]
    [InlineData("DELETE", "/api/v1/admin/ai/providers/{id}/credential")]
    [InlineData("GET", "/api/v1/admin/ai/providers/{id}/models")]
    [InlineData("PATCH", "/api/v1/admin/ai/models/{id}")]
    [InlineData("POST", "/api/v1/admin/ai/providers/{id}/models/actions/sync")]
    [InlineData("POST", "/api/v1/admin/ai/providers/{id}/models/actions/sync/apply")]
    public async Task ShouldPassAuthorization_WhenCallerIsAdministrator(string method, string pathTemplate)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("admin-1", "Administrator"));

        var response = await SendAsync(method, pathTemplate);

        // No live database in this environment (see CustomWebApplicationFactory) — proves
        // authorization let the admin through to the handler, never 401/403.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    private Task<HttpResponseMessage> SendAsync(string method, string pathTemplate)
    {
        var path = pathTemplate.Replace("{id}", SomeProviderId.ToString());

        return method switch
        {
            "GET" => _client.GetAsync(path),
            "PATCH" when path.Contains("/models/") => _client.PatchAsync(path, JsonContent.Create(new UpdateAiModelStatusRequest(AIModelStatus.Deprecated))),
            "PATCH" => _client.PatchAsync(path, JsonContent.Create(new UpdateAiProviderRequest(true, null))),
            "PUT" => _client.PutAsync(path, JsonContent.Create(new SetAiProviderCredentialRequest("test-key"))),
            "DELETE" => _client.DeleteAsync(path),
            "POST" when path.EndsWith("/apply", StringComparison.Ordinal) => _client.PostAsync(path, JsonContent.Create(new ApplyProviderModelSyncRequest([], []))),
            "POST" => _client.PostAsync(path, content: null),
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };
    }
}
