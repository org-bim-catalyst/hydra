using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AskLucy.Web.Contracts;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Ai;

/// <summary>
/// specs/070 — the admin voice page's endpoints. Same self-signed-JWT pattern as
/// <see cref="AdminAiProvidersControllerTests"/>. The administrator requests target an unknown
/// engine or provider id, so passing authorization never writes a row to the shared database.
/// </summary>
public sealed class AdminVoiceProvidersControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly Guid SomeProviderId = Guid.NewGuid();

    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", "/api/v1/admin/voice/engines" },
        { "GET", "/api/v1/admin/voice/providers" },
        { "POST", "/api/v1/admin/voice/providers" },
        { "PUT", "/api/v1/admin/voice/providers/{id}/credential" },
        { "GET", "/api/v1/admin/voice/providers/{id}/voices" },
        { "PUT", "/api/v1/admin/voice/primary" },
        { "POST", "/api/v1/admin/voice/providers/{id}/preview" },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ShouldReturn401_WhenAnonymous(string method, string pathTemplate)
    {
        var response = await SendAsync(method, pathTemplate);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ShouldReturn403_WhenCallerHasNoAdminRole(string method, string pathTemplate)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await SendAsync(method, pathTemplate);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ShouldPassAuthorization_WhenCallerIsAdministrator(string method, string pathTemplate)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("admin-1", "Administrator"));

        var response = await SendAsync(method, pathTemplate);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetEngines_ShouldListEveryEngineInstalledOnTheServer()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("admin-1", "Administrator"));

        var response = await _client.GetAsync("/api/v1/admin/voice/engines");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"providerKey\":\"ElevenLabs\"").And.Contain("\"providerKey\":\"Supertonic\"");
    }

    [Fact]
    public async Task AddProvider_ShouldReturn404_ForAnEngineThisServerDoesNotHave()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("admin-1", "Administrator"));

        var response = await _client.PostAsync("/api/v1/admin/voice/providers", JsonContent.Create(new AddVoiceProviderRequest("NotAnEngine", null)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Preview_ShouldReturn400_ForAnUnspeakableSentence()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("admin-1", "Administrator"));

        var response = await _client.PostAsync(
            $"/api/v1/admin/voice/providers/{SomeProviderId}/preview",
            JsonContent.Create(new PreviewVoiceRequest("F1", "... !!", "en")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private Task<HttpResponseMessage> SendAsync(string method, string pathTemplate)
    {
        var path = pathTemplate.Replace("{id}", SomeProviderId.ToString());

        return method switch
        {
            "GET" => _client.GetAsync(path),
            "POST" when path.EndsWith("/preview", StringComparison.Ordinal) => _client.PostAsync(path, JsonContent.Create(new PreviewVoiceRequest("F1", "Hello.", "en"))),
            "POST" => _client.PostAsync(path, JsonContent.Create(new AddVoiceProviderRequest("NotAnEngine", null))),
            "PUT" when path.EndsWith("/primary", StringComparison.Ordinal) => _client.PutAsync(path, JsonContent.Create(new SetPrimaryVoiceProviderRequest(SomeProviderId, "F1"))),
            "PUT" => _client.PutAsync(path, JsonContent.Create(new SetAiProviderCredentialRequest("test-key"))),
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };
    }
}
