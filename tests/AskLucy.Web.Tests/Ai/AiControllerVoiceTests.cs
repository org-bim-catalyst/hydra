using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AskLucy.Application.Ai;
using AskLucy.Web.Contracts;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Ai;

/// <summary>
/// tasks.md T035/T036 — contracts/voice-stt-session.md, contracts/voice-reply-stream.md.
/// Same no-live-database, self-signed-JWT pattern as <see cref="AdminAiProvidersControllerTests"/>
/// and <see cref="AiProvidersControllerTests"/>: proves the auth gate on these two new
/// AI-invoking endpoints, not the full ElevenLabs/database round trip (covered by the
/// Application/Infrastructure unit and integration tests instead).
/// </summary>
public sealed class AiControllerVoiceTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateVoiceSttSession_ShouldReturn401_WhenAnonymous()
    {
        var response = await _client.PostAsync(
            "/api/v1/ai/voice/stt-session", JsonContent.Create(new CreateSpeechToTextSessionRequest("en")), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateVoiceSttSession_ShouldPassAuthorization_WhenCallerIsAuthenticated()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await _client.PostAsync(
            "/api/v1/ai/voice/stt-session", JsonContent.Create(new CreateSpeechToTextSessionRequest("en")), TestContext.Current.CancellationToken);

        // No live ElevenLabs connectivity in this environment — proves the request reached
        // the handler rather than being rejected at the auth gate.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task VoiceReply_ShouldReturn401_WhenAnonymous()
    {
        var request = new VoiceReplyRequest(
            Guid.NewGuid(), [new ChatMessageDto("user", "Hi")], Guid.NewGuid(), Guid.NewGuid(), null, "en");

        var response = await _client.PostAsync("/api/v1/ai/voice/reply", JsonContent.Create(request), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task VoiceReply_ShouldPassAuthorization_WhenCallerIsAuthenticated()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));
        var request = new VoiceReplyRequest(
            Guid.NewGuid(), [new ChatMessageDto("user", "Hi")], Guid.NewGuid(), Guid.NewGuid(), null, "en");

        var response = await _client.PostAsync("/api/v1/ai/voice/reply", JsonContent.Create(request), TestContext.Current.CancellationToken);

        // No live database in this environment (see CustomWebApplicationFactory) — proves
        // authorization let the request through to the handler, never 401/403.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetVoicePreferences_ShouldReturn401_WhenAnonymous()
    {
        var response = await _client.GetAsync("/api/v1/ai/voice/preferences", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetVoicePreferences_ShouldPassAuthorization_WhenCallerIsAuthenticated()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await _client.GetAsync("/api/v1/ai/voice/preferences", TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SaveVoicePreferences_ShouldReturn401_WhenAnonymous()
    {
        var response = await _client.PutAsync(
            "/api/v1/ai/voice/preferences",
            JsonContent.Create(new SaveVoicePreferenceRequest("PushToTalk", false, null, null, null, null, null, "fr")),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SaveVoicePreferences_ShouldPassAuthorization_WhenCallerIsAuthenticated()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await _client.PutAsync(
            "/api/v1/ai/voice/preferences",
            JsonContent.Create(new SaveVoicePreferenceRequest("PushToTalk", false, null, null, null, null, null, "fr")),
            TestContext.Current.CancellationToken);

        // No live database in this environment (see CustomWebApplicationFactory) — proves
        // authorization let the request through to the handler, never 401/403.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    /// <summary>specs/026-floating-chat-assistant FR-017 — FluentValidation runs as a
    /// MediatR pipeline behavior ahead of any repository/database access, so this proves
    /// the 400 without requiring a live database (constitution §2.VIII: rejected, never
    /// silently coerced).</summary>
    [Fact]
    public async Task SaveVoicePreferences_ShouldReturn400_WhenDefaultLanguageIsUnsupported()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await _client.PutAsync(
            "/api/v1/ai/voice/preferences",
            JsonContent.Create(new SaveVoicePreferenceRequest("PushToTalk", false, null, null, null, null, null, "not-a-real-language")),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetVoiceProviderHealth_ShouldReturn401_WhenAnonymous()
    {
        var response = await _client.GetAsync("/api/v1/ai/voice/health", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetVoiceProviderHealth_ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await _client.GetAsync("/api/v1/ai/voice/health", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetVoiceProviderHealth_ShouldPassAuthorization_WhenCallerIsAdministrator()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("admin-1", "Administrator"));

        var response = await _client.GetAsync("/api/v1/ai/voice/health", TestContext.Current.CancellationToken);

        // No live database in this environment (see CustomWebApplicationFactory) — proves
        // authorization let the admin through to the handler, never 401/403.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
