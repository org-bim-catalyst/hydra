using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.OperationalFailures;
using AskLucy.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.OperationalFailures;

/// <summary>
/// specs/074 T069 — triage over HTTP: manage gates every transition, view gates the badge summary,
/// a transition someone else got to first is a 409 <c>incident-conflict</c>, and one root-cause
/// resolve clears a 1,000-incident fault (SC-011).
/// </summary>
public sealed class AdminOperationalFailuresTriageEndpointsTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private const string BaseUrl = "/api/v1/admin/operational-failures";

    // Unique per run, so nothing here ever touches incidents from another test or the real pipeline.
    private readonly string _rootCauseKey = $"triage-test:{Guid.NewGuid():N}";
    private readonly HttpClient _client = factory.CreateClient();

    private void AuthenticateWith(params string[] permissions) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwtFactory.Create($"triage-{Guid.NewGuid():N}", ["Support"], permissions));

    [Theory]
    [InlineData("acknowledge")]
    [InlineData("resolve")]
    [InlineData("reopen")]
    public async Task Transition_ShouldReturn403_WithViewOnly(string action)
    {
        var incidentId = (await SeedAsync(1))[0];
        try
        {
            AuthenticateWith(AdminPermissionCatalog.OperationalFailuresView);

            var response = await _client.PostAsync($"{BaseUrl}/incidents/{incidentId}/actions/{action}", null, TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Fact]
    public async Task RootCauseResolve_ShouldReturn403_WithViewOnly()
    {
        AuthenticateWith(AdminPermissionCatalog.OperationalFailuresView);

        var response = await _client.PostAsync($"{BaseUrl}/root-causes/{_rootCauseKey}/actions/resolve", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Transitions_ShouldMoveTheIncident_WithManage()
    {
        var incidentId = (await SeedAsync(1))[0];
        try
        {
            AuthenticateWith(AdminPermissionCatalog.OperationalFailuresView, AdminPermissionCatalog.OperationalFailuresManage);
            var ct = TestContext.Current.CancellationToken;

            var acknowledged = await _client.PostAsync($"{BaseUrl}/incidents/{incidentId}/actions/acknowledge", null, ct);
            acknowledged.StatusCode.Should().Be(HttpStatusCode.OK);
            (await StateAsync(acknowledged)).Should().Be("Acknowledged");

            var resolved = await _client.PostAsJsonAsync($"{BaseUrl}/incidents/{incidentId}/actions/resolve", new { note = "Rotated the key." }, ct);
            resolved.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var body = await ReadJsonAsync(resolved))
            {
                body.RootElement.GetProperty("state").GetString().Should().Be("Resolved");
                body.RootElement.GetProperty("resolved").GetProperty("note").GetString().Should().Be("Rotated the key.");
            }

            var reopened = await _client.PostAsync($"{BaseUrl}/incidents/{incidentId}/actions/reopen", null, ct);
            reopened.StatusCode.Should().Be(HttpStatusCode.OK);
            (await StateAsync(reopened)).Should().Be("Open");
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Fact]
    public async Task Transition_ThatSomeoneElseGotToFirst_ShouldBeAnIncidentConflict()
    {
        var incidentId = (await SeedAsync(1))[0];
        try
        {
            AuthenticateWith(AdminPermissionCatalog.OperationalFailuresView, AdminPermissionCatalog.OperationalFailuresManage);
            var ct = TestContext.Current.CancellationToken;

            (await _client.PostAsync($"{BaseUrl}/incidents/{incidentId}/actions/acknowledge", null, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
            var second = await _client.PostAsync($"{BaseUrl}/incidents/{incidentId}/actions/acknowledge", null, ct);

            second.StatusCode.Should().Be(HttpStatusCode.Conflict);
            using var body = await ReadJsonAsync(second);
            body.RootElement.GetProperty("type").GetString().Should().EndWith("/incident-conflict");
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Fact]
    public async Task Resolve_WithANoteOver500Characters_ShouldBe400()
    {
        var incidentId = (await SeedAsync(1))[0];
        try
        {
            AuthenticateWith(AdminPermissionCatalog.OperationalFailuresView, AdminPermissionCatalog.OperationalFailuresManage);

            var response = await _client.PostAsJsonAsync(
                $"{BaseUrl}/incidents/{incidentId}/actions/resolve", new { note = new string('n', 501) }, TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [Fact]
    public async Task Summary_ShouldReturn403_WithoutView()
    {
        var otherKey = AdminPermissionCatalog.All.First(p => !p.Key.StartsWith("admin.operational-failures", StringComparison.Ordinal)).Key;
        AuthenticateWith(otherKey);

        var response = await _client.GetAsync($"{BaseUrl}/summary", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Related_ShouldListTheOtherUnresolvedIncidentsSharingTheCause()
    {
        var ids = await SeedAsync(3);
        try
        {
            AuthenticateWith(AdminPermissionCatalog.OperationalFailuresView);

            var response = await _client.GetAsync($"{BaseUrl}/incidents/{ids[0]}/related", TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var body = await ReadJsonAsync(response);
            body.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
            body.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())
                .Should().BeEquivalentTo(ids.Skip(1));
        }
        finally
        {
            await CleanupAsync();
        }
    }

    /// <summary>SC-011: one fault across 1,000 subjects is one badge entry, and one action clears it.</summary>
    [Fact]
    public async Task RootCauseResolve_OverAThousandIncidents_ShouldResolveThemAll_AndClearTheirBadgeEntry()
    {
        const int incidentCount = 1_000;
        await SeedAsync(incidentCount);
        try
        {
            AuthenticateWith(AdminPermissionCatalog.OperationalFailuresView, AdminPermissionCatalog.OperationalFailuresManage);
            var ct = TestContext.Current.CancellationToken;

            // Other tests share this database, so the badge is judged by whether this cause is still in it.
            (await BadgeAsync()).Should().BeGreaterThanOrEqualTo(1);
            (await HasUnacknowledgedCriticalAsync()).Should().BeTrue();

            var stopwatch = Stopwatch.StartNew();
            var response = await _client.PostAsJsonAsync($"{BaseUrl}/root-causes/{_rootCauseKey}/actions/resolve", new { note = "Rotated." }, ct);
            stopwatch.Stop();

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var body = await ReadJsonAsync(response))
            {
                body.RootElement.GetProperty("attempted").GetInt32().Should().Be(incidentCount);
                body.RootElement.GetProperty("succeeded").GetInt32().Should().Be(incidentCount);
                body.RootElement.GetProperty("failed").GetArrayLength().Should().Be(0);
            }

            (await HasUnacknowledgedCriticalAsync()).Should().BeFalse();
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(1), "a root-cause resolve is one request, even at N = 1,000");
        }
        finally
        {
            await CleanupAsync();
        }
    }

    private async Task<Guid[]> SeedAsync(int count)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var now = DateTime.UtcNow;

        var incidents = Enumerable.Range(0, count).Select(i =>
        {
            var incident = OperationalFailureIncident.Open(
                $"{_rootCauseKey}:{i}", _rootCauseKey, OperationalFailureEngine.DocumentProcessing, "Document indexing",
                OperationalFailureKind.CredentialRejected, OperationalFailureSeverity.Critical, now.AddSeconds(-i),
                "The provider rejected the credential", "corr-triage-test", providerName: "OpenAI");
            incident.Id = Guid.NewGuid();
            typeof(OperationalFailureIncident).GetProperty(nameof(OperationalFailureIncident.OccurrenceCount))!
                .GetSetMethod(nonPublic: true)!.Invoke(incident, [1]);
            return incident;
        }).ToArray();

        db.OperationalFailureIncidents.AddRange(incidents);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return [.. incidents.Select(i => i.Id)];
    }

    private async Task<bool> HasUnacknowledgedCriticalAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        return await db.OperationalFailureIncidents.AnyAsync(
            i => i.RootCauseKey == _rootCauseKey && i.TriageState == IncidentTriageState.Open, TestContext.Current.CancellationToken);
    }

    private async Task<int> BadgeAsync()
    {
        var response = await _client.GetAsync($"{BaseUrl}/summary", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("unacknowledgedCriticalRootCauses").GetInt32();
    }

    private static async Task<string?> StateAsync(HttpResponseMessage response)
    {
        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("state").GetString();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), cancellationToken: TestContext.Current.CancellationToken);

    private async Task CleanupAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        await db.OperationalFailureIncidents.Where(i => i.RootCauseKey == _rootCauseKey).ExecuteDeleteAsync(CancellationToken.None);
    }
}
