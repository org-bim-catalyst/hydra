using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Ai;

/// <summary>
/// FR-015 / User Story 2: every AI-invoking endpoint must reject unauthenticated
/// requests before reaching a handler. Deliberately does not require a live database —
/// the JWT auth gate runs first, in the middleware pipeline.
/// </summary>
public sealed class AnonymousAccessTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Chat_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/ai/chat", new { messages = new[] { new { role = "user", content = "hi" } } }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Translate_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/ai/translate", new { text = "hi", targetLanguage = "fr" }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GenerateImage_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/ai/images", new { prompt = "a cat" }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Transcribe_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent([1, 2, 3]), "file", "clip.wav" },
        };

        var response = await _client.PostAsync("/api/v1/ai/transcriptions", content, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Chats_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync("/api/v1/chats", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
