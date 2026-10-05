using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Appearance;
using AskLucy.Domain.Appearance;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NSubstitute;
using Xunit;

namespace AskLucy.Web.Tests.Appearance;

/// <summary>specs/080 contracts/presence-sphere-api.md over a real host, with the repository replaced.</summary>
public sealed class AppearanceEndpointsTests : IClassFixture<AppearanceApiFactory>
{
    private const string Url = "/api/v1/appearance/presence-sphere";

    private readonly AppearanceApiFactory _factory;
    private readonly HttpClient _client;

    public AppearanceEndpointsTests(AppearanceApiFactory factory)
    {
        factory.Reset();
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private HttpRequestMessage Request(HttpMethod method, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, Url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static string User() => TestJwtFactory.Create("user-1", "User");

    private static string ViewOnly() => TestJwtFactory.Create("viewer-1", ["User"], ["admin.appearance.view"]);

    private static string Manager() => TestJwtFactory.Create("manager-1", ["User"], ["admin.appearance.manage"]);

    private static readonly object Valid = new { dotSizeMultiplier = 0.8, cardFillPercent = 70, zoomEnabled = true };

    [Fact]
    public async Task Get_ShouldReturn401_WhenNotSignedIn()
    {
        var response = await _client.SendAsync(Request(HttpMethod.Get, token: null), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_ShouldReturnTheDefaults_ForAnySignedInUser_WhenNothingIsSaved()
    {
        _factory.Settings.GetAsync(Arg.Any<CancellationToken>()).Returns((PresenceSphereSettings?)null);

        var response = await _client.SendAsync(Request(HttpMethod.Get, User()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<PresenceSphereSettingsDto>(TestContext.Current.CancellationToken);
        dto.Should().BeEquivalentTo(PresenceSphereSettingsDto.Defaults);
        dto!.IsDefault.Should().BeTrue();
        dto.DotSizeMultiplier.Should().Be(1.00m);
        dto.CardFillPercent.Should().Be(75);
        dto.ZoomEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Get_ShouldReturnTheSavedValues()
    {
        var saved = PresenceSphereSettings.Create(0.5m, 60, true, "manager-1", new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc));
        _factory.Settings.GetAsync(Arg.Any<CancellationToken>()).Returns(saved);

        var response = await _client.SendAsync(Request(HttpMethod.Get, User()), TestContext.Current.CancellationToken);

        var dto = await response.Content.ReadFromJsonAsync<PresenceSphereSettingsDto>(TestContext.Current.CancellationToken);
        dto!.DotSizeMultiplier.Should().Be(0.5m);
        dto.CardFillPercent.Should().Be(60);
        dto.ZoomEnabled.Should().BeTrue();
        dto.IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task Put_ShouldReturn401_WhenNotSignedIn()
    {
        var response = await _client.SendAsync(Request(HttpMethod.Put, token: null, Valid), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("viewOnly")]
    public async Task Put_ShouldReturn403_WithoutTheManagePermission_AndSaveNothing(string caller)
    {
        var token = caller == "plain" ? User() : ViewOnly();

        var response = await _client.SendAsync(Request(HttpMethod.Put, token, Valid), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Settings.DidNotReceive().Add(Arg.Any<PresenceSphereSettings>());
    }

    [Fact]
    public async Task Put_ShouldSaveTheFirstSettings_WhenTheCallerCanManageAppearance()
    {
        _factory.Settings.GetAsync(Arg.Any<CancellationToken>()).Returns((PresenceSphereSettings?)null);

        var response = await _client.SendAsync(Request(HttpMethod.Put, Manager(), Valid), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Settings.Received(1).Add(Arg.Is<PresenceSphereSettings>(s =>
            s.DotSizeMultiplier == 0.8m && s.CardFillPercent == 70 && s.ZoomEnabled && s.ModifiedBy == "manager-1"));
        var dto = await response.Content.ReadFromJsonAsync<PresenceSphereSettingsDto>(TestContext.Current.CancellationToken);
        dto!.IsDefault.Should().BeFalse();
        dto.CardFillPercent.Should().Be(70);
    }

    [Fact]
    public async Task Put_ShouldUpdateTheExistingRow()
    {
        var existing = PresenceSphereSettings.Create(1.00m, 75, false, "someone", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        _factory.Settings.GetAsync(Arg.Any<CancellationToken>()).Returns(existing);

        var response = await _client.SendAsync(Request(HttpMethod.Put, Manager(), Valid), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        existing.DotSizeMultiplier.Should().Be(0.8m);
        existing.ZoomEnabled.Should().BeTrue();
        existing.ModifiedBy.Should().Be("manager-1");
        _factory.Settings.DidNotReceive().Add(Arg.Any<PresenceSphereSettings>());
    }

    [Theory]
    [InlineData(0.24, 70, "dotSizeMultiplier")]
    [InlineData(2.01, 70, "dotSizeMultiplier")]
    [InlineData(1.0, 39, "cardFillPercent")]
    [InlineData(1.0, 96, "cardFillPercent")]
    public async Task Put_ShouldReturn400NamingTheRange_ForAnOutOfRangeValue_AndSaveNothing(double dotSize, int fill, string field)
    {
        var body = new { dotSizeMultiplier = dotSize, cardFillPercent = fill, zoomEnabled = false };

        var response = await _client.SendAsync(Request(HttpMethod.Put, Manager(), body), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(field).And.Contain("between");
        _factory.Settings.DidNotReceive().Add(Arg.Any<PresenceSphereSettings>());
    }
}
