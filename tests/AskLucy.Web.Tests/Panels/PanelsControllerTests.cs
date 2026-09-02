using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Panels;
using AskLucy.Domain.Panels;
using AskLucy.Web.Contracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace AskLucy.Web.Tests.Panels;

/// <summary>
/// specs/028-ai-floating-panels contracts/panel-preferences-api.md. Same no-live-database pattern
/// as <see cref="Weather.WeatherControllerTests"/> — <see cref="IUserPanelPreferenceRepository"/>/
/// <see cref="IUnitOfWork"/> are substituted via <c>ConfigureTestServices</c> so these never make a
/// real EF Core/SQL Server call.
/// </summary>
public sealed class PanelsControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private HttpClient CreateClientWithRepository(IUserPanelPreferenceRepository repository)
    {
        var customFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUserPanelPreferenceRepository>();
                services.AddScoped(_ => repository);
                services.RemoveAll<IUnitOfWork>();
                services.AddScoped(_ => Substitute.For<IUnitOfWork>());
            }));
        return customFactory.CreateClient();
    }

    private static void Authorize(HttpClient client) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

    [Fact]
    public async Task GetPreferences_ShouldReturn401_WhenNoBearerTokenIsProvided()
    {
        var response = await _client.GetAsync("/api/v1/panels/preferences", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPreferences_ShouldReturn200WithTheDefault_WhenNoPreferenceRowExistsYet()
    {
        var repository = Substitute.For<IUserPanelPreferenceRepository>();
        repository.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns((UserPanelPreference?)null);
        var client = CreateClientWithRepository(repository);
        Authorize(client);

        var response = await client.GetAsync("/api/v1/panels/preferences", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UserPanelPreferenceDto>(TestContext.Current.CancellationToken);
        body!.OpacityPercent.Should().Be(UserPanelPreference.DefaultOpacityPercent);
    }

    [Fact]
    public async Task GetPreferences_ShouldReturn200_WithThePersistedOpacity()
    {
        var preference = UserPanelPreference.Create("user-1", "user-1");
        preference.SetOpacityPercent(55, "user-1");
        var repository = Substitute.For<IUserPanelPreferenceRepository>();
        repository.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(preference);
        var client = CreateClientWithRepository(repository);
        Authorize(client);

        var response = await client.GetAsync("/api/v1/panels/preferences", TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<UserPanelPreferenceDto>(TestContext.Current.CancellationToken);
        body!.OpacityPercent.Should().Be(55);
    }

    [Fact]
    public async Task SavePreferences_ShouldReturn200_AndPersistTheNewOpacity()
    {
        var repository = Substitute.For<IUserPanelPreferenceRepository>();
        repository.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns((UserPanelPreference?)null);
        var client = CreateClientWithRepository(repository);
        Authorize(client);

        var response = await client.PutAsJsonAsync("/api/v1/panels/preferences", new SavePanelPreferencesRequest(60), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UserPanelPreferenceDto>(TestContext.Current.CancellationToken);
        body!.OpacityPercent.Should().Be(60);
        repository.Received(1).Add(Arg.Is<UserPanelPreference>(p => p != null && p.OpacityPercent == 60));
    }

    [Fact]
    public async Task SavePreferences_ShouldReturn400ProblemDetails_ForAnOutOfRangeOpacity()
    {
        var repository = Substitute.For<IUserPanelPreferenceRepository>();
        var client = CreateClientWithRepository(repository);
        Authorize(client);

        var response = await client.PutAsJsonAsync("/api/v1/panels/preferences", new SavePanelPreferencesRequest(10), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}
