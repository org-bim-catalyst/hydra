using System.Net.Http;
using FluentAssertions;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Xunit;

namespace AskLucy.Web.Tests.Notifications;

/// <summary>
/// specs/067 T231 (constitution section 6 and 13): the generated OpenAPI document describes every route in
/// <c>contracts/notifications-api.md</c> and <c>contracts/admin-notifications-api.md</c> (user notifications,
/// preferences, localization, admin notifications, templates, announcements, audit) with the methods and
/// status codes the contracts document, and every error response is a Problem Details body.
/// <para>
/// <c>/openapi</c> is only mapped in Development, so the document is read from the registered document provider
/// (the same approach as <c>CustomModelsOpenApiTests</c>), not over HTTP. No database is touched.
/// </para>
/// <para>
/// Route notes. The admin contract lists its paths relative to <c>/api/v1/admin</c>; the notification and
/// localization ones live under <c>/api/v1/admin/notifications</c> (the contract's "Base" line plus the
/// <c>/notifications</c> prefix its headings use, with localization at <c>/notifications/localization</c>).
/// Status codes are the contract's per-endpoint codes plus the cross-cutting ones its summary names:
/// 401 on every route, 403 on admin routes (permission gate) and 429 on rate-limited ones.
/// </para>
/// </summary>
public sealed class NotificationsOpenApiTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private const string User = "/api/v1/notifications";
    private const string Prefs = "/api/v1/users/me/notification-preferences";
    private const string MyLocalization = "/api/v1/users/me/localization";
    private const string Admin = "/api/v1/admin/notifications";
    private const string Templates = Admin + "/templates";
    private const string Version = Templates + "/{templateId}/versions/{versionId}";

    private static readonly int[] UserCommon = [401, 429];
    private static readonly int[] AdminCommon = [401, 403, 429];

    /// <summary>(method, path, documented status codes). Every code must appear in the generated document.</summary>
    public static TheoryData<string, string, int[]> Contract() => new()
    {
        // contracts/notifications-api.md
        { "GET", User, [200, 400, .. UserCommon] },
        { "GET", User + "/unread-count", [200, .. UserCommon] },
        { "GET", User + "/{id}", [200, 404, .. UserCommon] },
        { "POST", User + "/{id}/actions/mark-read", [204, 404, .. UserCommon] },
        { "POST", User + "/actions/mark-all-read", [200, 400, .. UserCommon] },
        { "DELETE", User + "/{id}", [204, 404, .. UserCommon] },
        { "GET", Prefs, [200, .. UserCommon] },
        { "PUT", Prefs, [200, 422, .. UserCommon] },
        { "GET", MyLocalization, [200, .. UserCommon] },
        { "PUT", MyLocalization, [200, 422, .. UserCommon] },

        // contracts/admin-notifications-api.md: statistics, health, deliveries, audit
        { "GET", Admin + "/statistics", [200, 400, .. AdminCommon] },
        { "GET", Admin + "/channels", [200, .. AdminCommon] },
        { "GET", Admin + "/deliveries", [200, 400, .. AdminCommon] },
        { "GET", Admin + "/deliveries/{deliveryId}", [200, 404, .. AdminCommon] },
        { "POST", Admin + "/deliveries/{deliveryId}/actions/retry", [202, 404, 409, .. AdminCommon] },
        { "POST", Admin + "/deliveries/actions/retry", [200, 400, 409, .. AdminCommon] },
        { "GET", Admin + "/audit", [200, 400, .. AdminCommon] },

        // templates
        { "GET", Templates, [200, .. AdminCommon] },
        { "GET", Templates + "/{templateId}", [200, 404, .. AdminCommon] },
        { "GET", Version, [200, 404, .. AdminCommon] },
        { "POST", Templates + "/{templateId}/versions", [201, 404, 422, .. AdminCommon] },
        { "PUT", Version, [200, 404, 409, 422, 428, .. AdminCommon] },
        { "POST", Version + "/actions/preview", [200, 404, 422, .. AdminCommon] },
        { "POST", Version + "/actions/send-test", [202, 404, 422, .. AdminCommon] },
        { "POST", Version + "/actions/publish", [200, 404, 409, 428, .. AdminCommon] },
        { "POST", Version + "/actions/archive", [200, 404, 409, 428, .. AdminCommon] },

        // announcements (immutable: no PUT or DELETE, asserted separately)
        { "GET", Admin + "/announcements", [200, 400, .. AdminCommon] },
        { "POST", Admin + "/announcements", [201, 422, .. AdminCommon] },

        // localization settings
        { "GET", Admin + "/localization", [200, .. AdminCommon] },
        { "PUT", Admin + "/localization", [200, 409, 422, 428, .. AdminCommon] },
    };

    [Theory]
    [MemberData(nameof(Contract))]
    public async Task EveryContractRoute_IsDocumented_WithItsMethodAndStatusCodes(string method, string path, int[] statusCodes)
    {
        var document = await LoadAsync();

        document.Paths.Should().ContainKey(path, "contracts/*.md documents it");
        var operations = document.Paths[path].Operations!;
        operations.Should().ContainKey(new HttpMethod(method), $"{method} {path} is in the contract");

        var documented = operations[new HttpMethod(method)].Responses!.Keys.ToHashSet(StringComparer.Ordinal);
        var missing = statusCodes.Where(code => !documented.Contains(code.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();
        missing.Should().BeEmpty($"{method} {path} documents {{{string.Join(", ", documented.Order())}}} but the contract names {{{string.Join(", ", statusCodes)}}}");
    }

    [Theory]
    [MemberData(nameof(Contract))]
    public async Task EveryContractErrorResponse_IsAProblemDetailsBody(string method, string path, int[] statusCodes)
    {
        var document = await LoadAsync();
        var responses = document.Paths[path].Operations![new HttpMethod(method)].Responses!;

        foreach (var code in statusCodes.Where(c => c >= 400))
        {
            var key = code.ToString(System.Globalization.CultureInfo.InvariantCulture);
            responses.Should().ContainKey(key);
            var content = responses[key].Content;
            content.Should().NotBeNull($"{method} {path} {code} must describe a body");
            content!.Keys.Should().Contain(
                type => type.Contains("json", StringComparison.OrdinalIgnoreCase),
                $"{method} {path} {code} must be a Problem Details (application/problem+json) response");
        }
    }

    [Fact]
    public async Task Announcements_AreImmutable_SoNoPutOrDeleteIsDocumented()
    {
        var document = await LoadAsync();

        var operations = document.Paths[Admin + "/announcements"].Operations!.Keys.Select(m => m.Method).ToList();
        operations.Should().BeEquivalentTo(["GET", "POST"]);
        document.Paths.Keys.Should().NotContain(p => p.StartsWith(Admin + "/announcements/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NothingBeyondTheContract_IsDocumentedUnderTheNotificationRoutes()
    {
        var document = await LoadAsync();
        var documentedRoutes = Contract().Select(row => (row.Data.Item2, row.Data.Item1)).ToHashSet();

        var extra = document.Paths
            .Where(p => p.Key.StartsWith(User, StringComparison.Ordinal) || p.Key.StartsWith(Admin, StringComparison.Ordinal)
                || p.Key.StartsWith(Prefs, StringComparison.Ordinal) || p.Key.StartsWith(MyLocalization, StringComparison.Ordinal))
            .SelectMany(p => p.Value.Operations!.Keys.Select(m => (p.Key, m.Method)))
            .Where(route => !documentedRoutes.Contains(route))
            .ToList();

        extra.Should().BeEmpty("a route the contract doesn't describe should be added to contracts/*.md and this table");
    }

    private async Task<OpenApiDocument> LoadAsync()
    {
        var provider = factory.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        return await provider.GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);
    }
}
