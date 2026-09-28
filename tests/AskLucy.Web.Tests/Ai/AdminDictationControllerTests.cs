using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Dictation;
using AskLucy.Domain.Ai.Dictation;
using AskLucy.Web.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using NSubstitute.ClearExtensions;
using Xunit;

namespace AskLucy.Web.Tests.Ai;

/// <summary>
/// One real host for <see cref="AdminDictationControllerTests"/>, with the dictation setting, the
/// Local Whisper model catalog and the trial replaced, so nothing writes the shared database's
/// dictation row or loads a model.
/// </summary>
public sealed class DictationApiFactory : CustomWebApplicationFactory
{
    public IDictationEngineSettingRepository Settings { get; } = Substitute.For<IDictationEngineSettingRepository>();

    public ILocalWhisperModelCatalog Catalog { get; } = Substitute.For<ILocalWhisperModelCatalog>();

    public ILocalWhisperModelTrial Trial { get; } = Substitute.For<ILocalWhisperModelTrial>();

    /// <summary>Forgets every stub and received call left by the previous test.</summary>
    public void Reset()
    {
        Settings.ClearSubstitute(ClearOptions.All);
        Catalog.ClearSubstitute(ClearOptions.All);
        Trial.ClearSubstitute(ClearOptions.All);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDictationEngineSettingRepository>();
            services.AddSingleton(Settings);
            services.RemoveAll<ILocalWhisperModelCatalog>();
            services.AddSingleton(Catalog);
            services.RemoveAll<ILocalWhisperModelTrial>();
            services.AddSingleton(Trial);
        });
    }
}

/// <summary>specs/078 T027 — the admin dictation routes (contracts/admin-dictation.md) over the real host.</summary>
public sealed class AdminDictationControllerTests : IClassFixture<DictationApiFactory>
{
    private static readonly byte[] RowVersion = [0, 0, 0, 0, 0, 0, 7, 209];
    private static readonly Guid SomeModelId = Guid.NewGuid();

    private readonly DictationEngineSetting _setting = DictationEngineSetting.CreateDefault(DateTime.UtcNow);
    private readonly DictationApiFactory _factory;
    private readonly HttpClient _client;

    public AdminDictationControllerTests(DictationApiFactory factory)
    {
        factory.Reset();
        _factory = factory;
        _setting.RowVersion = RowVersion;
        factory.Settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(_setting);
        factory.Catalog.ListOptionsAsync(Arg.Any<CancellationToken>()).Returns([]);
        factory.Catalog.ResolveSelectedAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(LocalWhisperModelResolution.None.Instance);
        factory.Catalog.ResolveForTrialAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new LocalWhisperModelResolution.Unavailable("Only a completed deployment can be tried.", null));
        _client = factory.CreateClient();
    }

    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", "/api/v1/admin/voice/dictation" },
        { "PUT", "/api/v1/admin/voice/dictation/local-whisper-model" },
        { "POST", "/api/v1/admin/voice/dictation/try" },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ShouldReturn401_WhenAnonymous(string method, string path)
    {
        var response = await SendAsync(method, path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ShouldReturn403_WhenCallerHasNoAdminRole(string method, string path)
    {
        Authorize("user-1");

        var response = await SendAsync(method, path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ShouldPassAuthorization_WhenCallerIsAdministrator(string method, string path)
    {
        Authorize("admin-1", "Administrator");

        var response = await SendAsync(method, path);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SelectingAModel_ShouldReturn403_ForAViewOnlyCaller()
    {
        AuthorizeWith("admin.ai-providers.view");

        var response = await SendAsync("PUT", "/api/v1/admin/voice/dictation/local-whisper-model");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetDictation_ShouldDescribeAFreshDeployment()
    {
        Authorize("admin-1", "Administrator");

        var response = await _client.GetAsync("/api/v1/admin/voice/dictation", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("primaryEngine").GetString().Should().Be("LocalWhisper");
        body.RootElement.GetProperty("rowVersion").GetString().Should().Be(Convert.ToBase64String(RowVersion));
        body.RootElement.GetProperty("localWhisper").GetProperty("selectedModelId").ValueKind.Should().Be(JsonValueKind.Null);
        body.RootElement.GetProperty("engines").EnumerateArray().Select(e => e.GetProperty("engine").GetString())
            .Should().Equal("LocalWhisper", "OpenAiWhisper", "ElevenLabsRealtime");
    }

    [Fact]
    public async Task SelectingAModel_ShouldReturn409_ForAStaleRowVersion()
    {
        Authorize("admin-1", "Administrator");

        var response = await _client.PutAsync(
            "/api/v1/admin/voice/dictation/local-whisper-model",
            JsonContent.Create(new SelectLocalWhisperModelRequest(null, Convert.ToBase64String([0, 0, 0, 0, 0, 0, 0, 1]))),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SelectingAModel_ShouldReturn422_WithTheReason_WhenItCantBeSelected()
    {
        _factory.Catalog.CheckSelectableAsync(SomeModelId, Arg.Any<CancellationToken>())
            .Returns(new LocalWhisperModelOption(SomeModelId, "supertonic-3", false, "Deploy the model from a URL that names its .bin file."));
        Authorize("admin-1", "Administrator");

        var response = await _client.PutAsync(
            "/api/v1/admin/voice/dictation/local-whisper-model",
            JsonContent.Create(new SelectLocalWhisperModelRequest(SomeModelId, Convert.ToBase64String(RowVersion))),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("local-whisper-model-not-selectable").And.Contain("names its .bin file");
        _setting.LocalWhisperModelId.Should().BeNull();
    }

    [Fact]
    public async Task Try_ShouldReturnTheTranscript_WithoutChangingTheSetting()
    {
        _factory.Catalog.ResolveForTrialAsync(SomeModelId, Arg.Any<CancellationToken>())
            .Returns(new LocalWhisperModelResolution.Ready("C:/m/ggml-small.bin", "whisper.cpp (ggml-small.bin)", "ggml-small.bin"));
        _factory.Trial.TryAsync("C:/m/ggml-small.bin", Arg.Any<Stream>(), "ar", Arg.Any<CancellationToken>())
            .Returns(new DictationTranscript("marhaba", "ar", TimeSpan.FromMilliseconds(1840)));
        Authorize("admin-1", "Administrator");

        var response = await _client.PostAsync("/api/v1/admin/voice/dictation/try", TryForm(Wav(), SomeModelId, "ar"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("text").GetString().Should().Be("marhaba");
        body.RootElement.GetProperty("modelLabel").GetString().Should().Be("whisper.cpp (ggml-small.bin)");
        await _factory.Settings.DidNotReceiveWithAnyArgs().GetOrCreateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A real server answers 413 from the request size limit before the body is read; the test
    /// server doesn't enforce that limit, so the multipart limit refuses it during binding (400).
    /// </summary>
    [Fact]
    public async Task Try_ShouldRefuseASampleOverTheLimit()
    {
        Authorize("admin-1", "Administrator");

        var response = await _client.PostAsync(
            "/api/v1/admin/voice/dictation/try",
            TryForm(new byte[(4 * 1024 * 1024) + 1], SomeModelId, null),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.BadRequest);
        await _factory.Trial.DidNotReceiveWithAnyArgs().TryAsync(default!, default!, default, TestContext.Current.CancellationToken);
    }

    private static MultipartFormDataContent TryForm(byte[] audio, Guid customModelId, string? language)
    {
        var file = new ByteArrayContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        var form = new MultipartFormDataContent
        {
            { file, "file", "sample.wav" },
            { new StringContent(customModelId.ToString()), "customModelId" },
        };
        if (language is not null)
        {
            form.Add(new StringContent(language), "language");
        }

        return form;
    }

    /// <summary>A 0.1 s 16 kHz mono 16-bit PCM WAV of silence.</summary>
    private static byte[] Wav()
    {
        const int dataLength = 3200;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + dataLength);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(16000);
            writer.Write(32000);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(dataLength);
            writer.Write(new byte[dataLength]);
        }

        return stream.ToArray();
    }

    private void Authorize(string userId, params string[] roles) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create(userId, roles));

    /// <summary>A synthetic custom-role principal holding only <paramref name="permissions"/>.</summary>
    private void AuthorizeWith(params string[] permissions) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("custom-role-user", [], permissions));

    private Task<HttpResponseMessage> SendAsync(string method, string path) => method switch
    {
        "GET" => _client.GetAsync(path, TestContext.Current.CancellationToken),
        "PUT" => _client.PutAsync(path, JsonContent.Create(new SelectLocalWhisperModelRequest(null, Convert.ToBase64String(RowVersion))), TestContext.Current.CancellationToken),
        "POST" => _client.PostAsync(path, TryForm(Wav(), SomeModelId, null), TestContext.Current.CancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };
}
