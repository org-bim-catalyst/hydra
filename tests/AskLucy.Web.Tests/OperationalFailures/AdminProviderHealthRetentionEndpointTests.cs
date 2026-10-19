using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AskLucy.Domain.Authorization;
using AskLucy.Persistence;
using AskLucy.Web.Tests.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.OperationalFailures;

[Collection(DatabaseSerialGroup.Name)]
public sealed class AdminProviderHealthRetentionEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private const string Url = "/api/v1/admin/provider-health/retention";
    private const string StaleRowVersion = "AAAAAAAAAAA=";

    private readonly HttpClient _client = factory.CreateClient();

    private HttpClient As(string userId, params string[] permissions)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwtFactory.Create(userId, ["Support"], permissions));
        return _client;
    }

    private HttpClient AsViewer() => As("support-3", AdminPermissionCatalog.OperationalFailuresView);

    private HttpClient AsManager() => As(
        "support-4", AdminPermissionCatalog.OperationalFailuresView, AdminPermissionCatalog.OperationalFailuresManage);

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    private async Task<(int RetentionDays, string? RowVersion)> GetRetentionAsync(HttpClient client)
    {
        using var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadAsync(response);
        return (
            body.RootElement.GetProperty("retentionDays").GetInt32(),
            body.RootElement.GetProperty("rowVersion").GetString());
    }

    private async Task<HttpResponseMessage> PutAsync(HttpClient client, int retentionDays, string? rowVersion)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Url) { Content = Json(new { retentionDays }) };
        if (rowVersion is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", rowVersion);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<int> CountAuditRowsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        return await db.ProviderHealthRetentionAuditLogs.CountAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The shared test database keeps the singleton between runs; put it back after a mutating test.</summary>
    private async Task RestoreAsync(HttpClient client)
    {
        var (days, rowVersion) = await GetRetentionAsync(client);
        if (days == 30 || rowVersion is null)
        {
            return;
        }

        using var response = await PutAsync(client, 30, rowVersion);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_ReturnsTheStoredWindow_ForAViewer()
    {
        var client = AsViewer();

        using var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadAsync(response);
        body.RootElement.GetProperty("retentionDays").GetInt32().Should().Be(30);
        body.RootElement.GetProperty("source").GetString().Should().Be("Stored");
        body.RootElement.GetProperty("rowVersion").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Get_Returns403_WithoutTheViewPermission()
    {
        var client = As("user-9");

        using var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Put_UpdatesTheWindow_ForAManager_AndAuditsIt()
    {
        var client = AsManager();
        var (_, rowVersion) = await GetRetentionAsync(client);
        var auditRowsBefore = await CountAuditRowsAsync();

        try
        {
            using var response = await PutAsync(client, 14, rowVersion);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var body = await ReadAsync(response);
            body.RootElement.GetProperty("retentionDays").GetInt32().Should().Be(14);
            (await CountAuditRowsAsync()).Should().Be(auditRowsBefore + 1);

            var (storedDays, storedVersion) = await GetRetentionAsync(client);
            storedDays.Should().Be(14);
            storedVersion.Should().NotBe(rowVersion);
        }
        finally
        {
            await RestoreAsync(client);
        }
    }

    [Fact]
    public async Task Put_Returns403_WithoutTheManagePermission()
    {
        var client = AsViewer();

        using var response = await PutAsync(client, 14, StaleRowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Put_Returns428_WithoutIfMatch()
    {
        var client = AsManager();

        using var response = await PutAsync(client, 14, rowVersion: null);

        response.StatusCode.Should().Be((HttpStatusCode)428);
    }

    [Fact]
    public async Task Put_Returns409_WithAStaleVersion_AndWritesNoAuditRow()
    {
        var client = AsManager();
        var auditRowsBefore = await CountAuditRowsAsync();

        using var response = await PutAsync(client, 14, StaleRowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CountAuditRowsAsync()).Should().Be(auditRowsBefore);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public async Task Put_Returns400_OutsideTheRange_AndWritesNoAuditRow(int retentionDays)
    {
        var client = AsManager();
        var auditRowsBefore = await CountAuditRowsAsync();

        using var response = await PutAsync(client, retentionDays, StaleRowVersion);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountAuditRowsAsync()).Should().Be(auditRowsBefore);
    }
}
