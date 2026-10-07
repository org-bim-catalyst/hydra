using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Domain.Notifications;
using AskLucy.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.Localization;

/// <summary>
/// T185 — specs/067 US8, the localization endpoints over the real host and database: the admin switch (If-Match, 409, 422, the audit row)
/// and the caller's own language (422 while disabled or unsupported, then the effective language and direction).
/// </summary>
[Collection("LocalizationSetting")]
public sealed class LocalizationEndpointsTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private const string Admin = "/api/v1/admin/notifications/localization";
    private const string Me = "/api/v1/users/me/localization";
    private const string View = "admin.notifications.view";
    private const string Manage = "admin.notifications.manage";

    private readonly LocalizationTestHost _host = new(factory);

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private static HttpRequestMessage Put(string url, object body, string? rowVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        if (rowVersion is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", rowVersion);
        }

        return request;
    }

    private static async Task<JsonDocument> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    private async Task<string> RowVersionAsync(HttpClient admin)
    {
        using var doc = JsonDocument.Parse(await admin.GetStringAsync(Admin, TestContext.Current.CancellationToken));
        return doc.RootElement.GetProperty("rowVersion").GetString()!;
    }

    // ---- admin ----

    [Fact]
    public async Task Admin_Permissions_ViewReads_ManageChanges_NoneIsForbidden()
    {
        var ct = TestContext.Current.CancellationToken;

        (await factory.CreateClient().GetAsync(Admin, ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _host.AdminClient().GetAsync(Admin, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _host.AdminClient(Manage).GetAsync(Admin, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var viewer = _host.AdminClient(View);
        (await viewer.GetAsync(Admin, ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.SendAsync(Put(Admin, new { isEnabled = true, supportedLanguages = new[] { "en" } }, await RowVersionAsync(viewer)), ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_Get_ShowsTheDefault_AndOnlyTheLanguagesThePlatformProvides()
    {
        using var body = await JsonOf(await _host.AdminClient(View).GetAsync(Admin, TestContext.Current.CancellationToken));

        body.RootElement.GetProperty("isEnabled").GetBoolean().Should().BeFalse();
        body.RootElement.GetProperty("supportedLanguages").EnumerateArray().Select(e => e.GetString()).Should().Equal("en");
        var available = body.RootElement.GetProperty("availableLanguages").EnumerateArray().ToList();
        available.Select(l => l.GetProperty("code").GetString()).Should().Equal("en", "ar");
        available.Single(l => l.GetProperty("code").GetString() == "en").GetProperty("locked").GetBoolean().Should().BeTrue();
        available.Single(l => l.GetProperty("code").GetString() == "ar").GetProperty("nativeName").GetString().Should().Be("العربية");
        body.RootElement.GetProperty("rowVersion").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Admin_Put_NeedsIfMatch_AndAStaleOneIs409()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = _host.AdminClient(View, Manage);
        var token = await RowVersionAsync(admin);
        var change = new { isEnabled = true, supportedLanguages = new[] { "en", "ar" } };

        (await admin.PutAsJsonAsync(Admin, change, ct)).StatusCode.Should().Be((HttpStatusCode)428);

        (await admin.SendAsync(Put(Admin, change, token), ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var stale = await admin.SendAsync(Put(Admin, new { isEnabled = false, supportedLanguages = new[] { "en" } }, token), ct);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var problem = await JsonOf(stale);
        problem.RootElement.GetProperty("reason").GetString().Should().Be("ConcurrencyConflict");
    }

    [Theory]
    [InlineData(new[] { "ar" }, "English is always supported")]
    [InlineData(new string[0], "English is always supported")]
    [InlineData(new[] { "en", "fr" }, "'fr' is not a language the platform provides")]
    public async Task Admin_Put_RejectsAMissingEnglish_AndAnUnknownCode_With422(string[] languages, string expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = _host.AdminClient(View, Manage);

        var response = await admin.SendAsync(Put(Admin, new { isEnabled = true, supportedLanguages = languages }, await RowVersionAsync(admin)), ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain(expected);
    }

    [Fact]
    public async Task Admin_Put_AppliesAtOnce_AndIsAuditedWithBeforeAndAfter()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = _host.AdminClient(View, Manage);
        var adminId = _host.LastAdminId;
        var (user, _) = await _host.UserAsync();

        // Warm the cache with "disabled", then change the setting: the next read must already see the change.
        using (var before = await JsonOf(await user.GetAsync(Me, ct)))
        {
            before.RootElement.GetProperty("localizationEnabled").GetBoolean().Should().BeFalse();
        }

        var saved = await admin.SendAsync(Put(Admin, new { isEnabled = true, supportedLanguages = new[] { "ar", "en", "EN" } }, await RowVersionAsync(admin)), ct);

        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var body = await JsonOf(saved))
        {
            body.RootElement.GetProperty("isEnabled").GetBoolean().Should().BeTrue();
            body.RootElement.GetProperty("supportedLanguages").EnumerateArray().Select(e => e.GetString()).Should().Equal("en", "ar");
        }

        using (var after = await JsonOf(await user.GetAsync(Me, ct)))
        {
            after.RootElement.GetProperty("localizationEnabled").GetBoolean().Should().BeTrue();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var audit = await db.NotificationAuditLogs.AsNoTracking()
            .SingleAsync(a => a.ActorUserId == adminId && a.Action == NotificationAuditAction.LocalizationSettingChanged, ct);
        audit.DetailsJson.Should().Contain("before").And.Contain("after");
    }

    // ---- the caller's own language ----

    [Fact]
    public async Task Me_RequiresSignIn()
    {
        var ct = TestContext.Current.CancellationToken;

        (await factory.CreateClient().GetAsync(Me, ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await factory.CreateClient().PutAsJsonAsync(Me, new { preferredLanguage = "ar" }, ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WhileDisabled_IsEnglishOnly_AndChoosingIs422_ButAnEarlierChoiceIsEchoed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (user, _) = await _host.UserAsync(preferredLanguage: "ar");

        using var body = await JsonOf(await user.GetAsync(Me, ct));
        body.RootElement.GetProperty("localizationEnabled").GetBoolean().Should().BeFalse();
        body.RootElement.GetProperty("supportedLanguages").EnumerateArray().Select(l => l.GetProperty("code").GetString()).Should().Equal("en");
        body.RootElement.GetProperty("preferredLanguage").GetString().Should().Be("ar");
        body.RootElement.GetProperty("effectiveLanguage").GetString().Should().Be("en");
        body.RootElement.GetProperty("direction").GetString().Should().Be("ltr");

        (await user.PutAsJsonAsync(Me, new { preferredLanguage = "ar" }, ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Me_ChoosingArabic_SwitchesTheEffectiveLanguageAndDirection()
    {
        var ct = TestContext.Current.CancellationToken;
        await _host.SetPlatformAsync(true, "en", "ar");
        var (user, _) = await _host.UserAsync();

        var response = await user.PutAsJsonAsync(Me, new { preferredLanguage = "ar" }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonOf(response);
        body.RootElement.GetProperty("localizationEnabled").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("supportedLanguages").EnumerateArray().Select(l => l.GetProperty("code").GetString()).Should().Equal("en", "ar");
        body.RootElement.GetProperty("preferredLanguage").GetString().Should().Be("ar");
        body.RootElement.GetProperty("effectiveLanguage").GetString().Should().Be("ar");
        body.RootElement.GetProperty("direction").GetString().Should().Be("rtl");

        using var again = await JsonOf(await user.GetAsync(Me, ct));
        again.RootElement.GetProperty("preferredLanguage").GetString().Should().Be("ar");
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("xx")]
    [InlineData("")]
    public async Task Me_ChoosingAnUnsupportedOrMissingLanguage_IsRejected(string language)
    {
        await _host.SetPlatformAsync(true, "en", "ar");
        var (user, _) = await _host.UserAsync();

        var response = await user.PutAsJsonAsync(Me, new { preferredLanguage = language }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Me_ArabicNoLongerSupported_FallsBackToEnglish_KeepingTheChoice()
    {
        var ct = TestContext.Current.CancellationToken;
        await _host.SetPlatformAsync(true, "en", "ar");
        var (user, _) = await _host.UserAsync(preferredLanguage: "ar");
        await _host.SetPlatformAsync(true, "en");

        using var body = await JsonOf(await user.GetAsync(Me, ct));

        body.RootElement.GetProperty("effectiveLanguage").GetString().Should().Be("en");
        body.RootElement.GetProperty("preferredLanguage").GetString().Should().Be("ar");
        (await user.PutAsJsonAsync(Me, new { preferredLanguage = "ar" }, ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
