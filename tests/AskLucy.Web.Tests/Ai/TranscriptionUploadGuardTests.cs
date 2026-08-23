using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Ai;

/// <summary>
/// specs/034-transcription-crash-gesture-and-continuous-view T003 — proves
/// <c>AiController.Transcribe</c>/<c>TranscribeMicrophone</c> reject a missing/empty file part
/// with a specific 400 instead of an uncaught <see cref="NullReferenceException"/> falling
/// through to a generic 500 (the actual root cause behind the transcription-500 that survived
/// two prior fix rounds, both scoped to <c>OpenAIProvider.cs</c> — this gap is upstream of that
/// entirely, in ASP.NET Core's own silent-null <c>IFormFile</c> model binding).
/// </summary>
public sealed class TranscriptionUploadGuardTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private void AuthenticateAsUser() =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

    [Fact]
    public async Task Transcribe_ShouldReturn400_WhenNoFilePartIsPresent()
    {
        AuthenticateAsUser();
        using var form = new MultipartFormDataContent();

        var response = await _client.PostAsync("/api/v1/ai/transcriptions", form, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Transcribe_ShouldReturn400_WhenFileIsEmpty()
    {
        AuthenticateAsUser();
        using var form = new MultipartFormDataContent();
        using var emptyContent = new ByteArrayContent([]);
        emptyContent.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        form.Add(emptyContent, "file", "recording.webm");

        var response = await _client.PostAsync("/api/v1/ai/transcriptions", form, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TranscribeMicrophone_ShouldReturn400_WhenNoFilePartIsPresent()
    {
        AuthenticateAsUser();
        using var form = new MultipartFormDataContent();

        var response = await _client.PostAsync("/api/v1/ai/transcriptions/microphone", form, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Transcribe_ShouldReturn401_WhenAnonymous()
    {
        using var form = new MultipartFormDataContent();

        var response = await _client.PostAsync("/api/v1/ai/transcriptions", form, TestContext.Current.CancellationToken);

        // The auth gate runs before this feature's guard — confirms the guard doesn't
        // accidentally bypass or short-circuit authorization.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Deliberately no "well-formed file reaches the handler" test here: CustomWebApplicationFactory
    // does not substitute IAIProvider/OpenAIProvider with a test double, so a request that
    // clears this guard would fall through to a real outbound HTTP call to OpenAI — unsafe and
    // flaky in CI. The "well-formed input is unaffected" side of FR-002 is covered instead by
    // OpenAIProviderTests.cs (mocked HttpMessageHandler, no real network) — this guard sits
    // entirely upstream of that, in the controller, before any provider call is made.
}
