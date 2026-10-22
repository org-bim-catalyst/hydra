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

    [Fact]
    public async Task ShouldReturnTheToolSettings_ForAPrincipalHoldingOnlyTheViewPermission()
    {
        Authenticate("ai-tools-settings-viewer", [], [AdminPermissionCatalog.AiToolsView]);

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, Url + "/settings"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "body was: {0}\nrecent log:\n{1}",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            string.Join('\n', CapturedLogSink.Shared.Lines.TakeLast(20)));

        var settings = await response.Content.ReadFromJsonAsync<List<ToolSettingsResponse>>(
            TestContext.Current.CancellationToken);
        settings.Should().NotBeNull();
        settings!.Should().Contain(s => s.Tool == "map_snapshot", "the map snapshot tool declares settings");
        var mapSnapshot = settings!.First(s => s.Tool == "map_snapshot");
        mapSnapshot.Settings.Should().NotBeEmpty();
        mapSnapshot.Settings.Should().Contain(s => s.Key == "defaultSource");
    }

    [Fact]
    public async Task ShouldBeForbidden_SavingSettings_WithoutTheManagePermission()
    {
        Authenticate("ai-tools-settings-viewer", [], [AdminPermissionCatalog.AiToolsView]);

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Put, Url + "/map_snapshot/settings")
            {
                Content = JsonContent.Create(new { values = new Dictionary<string, string> { ["defaultSource"] = "google" } }),
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "body was: {0}\nrecent log:\n{1}",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            string.Join('\n', CapturedLogSink.Shared.Lines.TakeLast(20)));
    }

    [Fact]
    public async Task ShouldRefuseAValueTheToolDoesNotDeclare()
    {
        Authenticate("ai-tools-settings-admin", [], [AdminPermissionCatalog.AiToolsManage]);

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Put, Url + "/map_snapshot/settings")
            {
                Content = JsonContent.Create(new { values = new Dictionary<string, string> { ["notASetting"] = "x" } }),
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.BadRequest,
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

    private sealed record ToolSettingsResponse(string Tool, IReadOnlyList<ToolSettingResponse> Settings);

    private sealed record ToolSettingResponse(
        string Key,
        string ValueType,
        string Label,
        string Description,
        string Value,
        string DefaultValue);
}
