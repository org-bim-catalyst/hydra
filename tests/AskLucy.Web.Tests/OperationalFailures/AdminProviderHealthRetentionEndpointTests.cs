using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AskLucy.Domain.Authorization;
using AskLucy.Web.Tests.Authorization;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.OperationalFailures;

public sealed class AdminProviderHealthRetentionEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private const string Url = "/api/v1/admin/provider-health/retention";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Get_ReturnsTheConfiguredWindow_ForAViewer()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwtFactory.Create("support-3", ["Support"], [AdminPermissionCatalog.OperationalFailuresView]));

        var response = await _client.GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("retentionDays").GetInt32().Should().Be(30);
    }

    [Fact]
    public async Task Get_Returns403_WithoutTheViewPermission()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwtFactory.Create("user-9"));

        var response = await _client.GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
