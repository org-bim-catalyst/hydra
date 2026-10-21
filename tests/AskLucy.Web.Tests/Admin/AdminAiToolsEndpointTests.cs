using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AskLucy.Domain.Authorization;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Admin;

/// <summary>
/// specs/083 — the AI Tools admin read model: a principal holding only
/// <c>admin.ai-tools.view</c> may read the catalogue; a principal holding none of its
/// permissions may not. The rows come from the same merged catalogue the agent runtime
/// dispatches on, so the endpoint can never show a tool the runtime would refuse.
/// </summary>
[Collection(DatabaseSerialGroup.Name)]
public sealed class AdminAiToolsEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private const string Url = "/api/v1/admin/ai-tools";

    private readonly HttpClient _client = factory.CreateClient();

    private static HttpRequestMessage Get() => new(HttpMethod.Get, Url);

    private void Authenticate(string userId, string[] roles, string[] permissions)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtFactory.Create(userId, roles, permissions));
    }

    [Fact]
    public async Task ShouldReturnTheToolCatalogue_ForAPrincipalHoldingOnlyTheViewPermission()
    {
        Authenticate("ai-tools-viewer", [], [AdminPermissionCatalog.AiToolsView]);

        var response = await _client.SendAsync(Get(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "body was: {0}\nrecent log:\n{1}",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            string.Join('\n', CapturedLogSink.Shared.Lines.TakeLast(20)));

        var tools = await response.Content.ReadFromJsonAsync<List<AdminAiToolResponse>>(
            TestContext.Current.CancellationToken);
        tools.Should().NotBeNull();
        tools!.Should().NotBeEmpty("the platform registers native tools the runtime dispatches on");
    }

    [Fact]
    public async Task ShouldBeForbidden_ForAPrincipalHoldingNoPermissions()
    {
        Authenticate("ai-tools-nobody", [], []);

        var response = await _client.SendAsync(Get(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "body was: {0}\nrecent log:\n{1}",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            string.Join('\n', CapturedLogSink.Shared.Lines.TakeLast(20)));
    }

    private sealed record AdminAiToolResponse(
        string Name,
        string Description,
        string RiskLevel,
        string[] RequiredPermissions,
        string InputSchemaJson,
        string OutputSchemaJson);
}
