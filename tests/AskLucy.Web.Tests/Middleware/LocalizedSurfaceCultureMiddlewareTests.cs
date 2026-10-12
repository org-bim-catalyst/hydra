using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AskLucy.Web.Tests.Localization;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Middleware;

/// <summary>
/// T186 — specs/067 R15 over the real host: on <c>[LocalizedSurface]</c> endpoints an Arabic user gets Problem Details <c>title</c>/<c>detail</c> and
/// FluentValidation messages in Arabic; every other endpoint stays English; <c>traceId</c> and <c>reason</c> are never translated.
/// </summary>
[Collection("LocalizationSetting")]
public sealed partial class LocalizedSurfaceCultureMiddlewareTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly LocalizationTestHost _host = new(factory);

    [GeneratedRegex(@"\p{IsArabic}")]
    private static partial Regex ArabicLetters();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private static async Task<JsonDocument> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    [Fact]
    public async Task ALocalizedEndpoint_AnswersAnArabicUserInArabic_ExceptTheTraceId()
    {
        await _host.SetPlatformAsync(true, "en", "ar");
        var (arabic, _) = await _host.UserAsync(preferredLanguage: "ar");
        var (english, _) = await _host.UserAsync(preferredLanguage: "en");
        var missing = $"/api/v1/notifications/{Guid.NewGuid()}";
        var ct = TestContext.Current.CancellationToken;

        var inArabic = await arabic.GetAsync(missing, ct);
        var inEnglish = await english.GetAsync(missing, ct);

        inArabic.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var ar = await JsonOf(inArabic);
        using var en = await JsonOf(inEnglish);
        ar.RootElement.GetProperty("title").GetString().Should().Be("غير موجود");
        ar.RootElement.GetProperty("detail").GetString().Should().Be("لم يتم العثور على العنصر المطلوب.");
        en.RootElement.GetProperty("title").GetString().Should().Be("Not found");
        // The type and the trace id are identifiers, not text: they are the same in both languages.
        ar.RootElement.GetProperty("type").GetString().Should().Be(en.RootElement.GetProperty("type").GetString());
        ar.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty().And.NotMatchRegex(@"\p{IsArabic}");
    }

    [Fact]
    public async Task ValidationMessages_AreInArabic_OnALocalizedEndpoint()
    {
        await _host.SetPlatformAsync(true, "en", "ar");
        var (arabic, _) = await _host.UserAsync(preferredLanguage: "ar");

        // An empty language trips the request validator, whose built-in message FluentValidation ships in Arabic.
        var response = await arabic.PutAsJsonAsync("/api/v1/users/me/localization", new { preferredLanguage = "" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonOf(response);
        body.RootElement.GetProperty("title").GetString().Should().Be("فشل التحقق");
        body.RootElement.GetProperty("detail").GetString().Should().Be("حقل واحد أو أكثر غير صالح.");
        var messages = body.RootElement.GetProperty("errors").EnumerateObject().SelectMany(p => p.Value.EnumerateArray().Select(m => m.GetString()!)).ToList();
        messages.Should().NotBeEmpty().And.OnlyContain(m => ArabicLetters().IsMatch(m));
    }

    [Fact]
    public async Task ReasonCodes_StayUntranslated_WhileTheirTextIsLocalized()
    {
        await _host.SetPlatformAsync(true, "en", "ar");
        var (arabic, _) = await _host.UserAsync(preferredLanguage: "ar");

        // Localization is on and Arabic is supported, but French isn't: a 422 with its own (dynamic) detail.
        var response = await arabic.PutAsJsonAsync("/api/v1/users/me/localization", new { preferredLanguage = "fr" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = await JsonOf(response);
        body.RootElement.GetProperty("title").GetString().Should().Be("تم رفض تغيير اللغة");
        body.RootElement.GetProperty("type").GetString().Should().EndWith("/localization-rejected");
    }

    [Fact]
    public async Task ANonLocalizedEndpoint_StaysEnglish_ForAnArabicUser()
    {
        await _host.SetPlatformAsync(true, "en", "ar");
        var (arabic, _) = await _host.UserAsync(preferredLanguage: "ar");

        // Prompts are outside the Arabic scope: the same Arabic user gets the English problem text there.
        var response = await arabic.GetAsync($"/api/v1/prompts/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var body = await JsonOf(response);
        body.RootElement.GetProperty("title").GetString().Should().Be("Not found");
        body.RootElement.GetProperty("detail").GetString().Should().NotMatchRegex(@"\p{IsArabic}");
    }

    [Fact]
    public async Task WhileLocalizationIsOff_EvenAnArabicUserGetsEnglish()
    {
        var (arabic, _) = await _host.UserAsync(preferredLanguage: "ar");

        var response = await arabic.GetAsync($"/api/v1/notifications/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        using var body = await JsonOf(response);
        body.RootElement.GetProperty("title").GetString().Should().Be("Not found");
    }
}
