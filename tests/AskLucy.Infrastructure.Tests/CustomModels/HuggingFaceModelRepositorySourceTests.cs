using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Infrastructure.CustomModels.HuggingFace;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AskLucy.Infrastructure.Tests.CustomModels;

/// <summary>specs/072 T031 (happy path), T056 (redirect guards) and T064 (source failures), against a stubbed handler.</summary>
public sealed class HuggingFaceModelRepositorySourceTests
{
    private const string RepositoryId = "Supertone/supertonic-3";
    private const string CommitSha = "3cadd1ee6394adea1bd021217a0e650ede09a323";

    // Trimmed from GET https://huggingface.co/api/models/Supertone/supertonic-3/revision/main, captured 2026-09-23.
    private const string RevisionFixture = """
        {"_id":"69fba89c79ec6f48e7593cd0","id":"Supertone/supertonic-3","private":false,"pipeline_tag":"text-to-speech",
         "library_name":"supertonic","modelId":"Supertone/supertonic-3","author":"Supertone",
         "sha":"3cadd1ee6394adea1bd021217a0e650ede09a323","lastModified":"2026-05-18T08:59:01.000Z","gated":false,"disabled":false}
        """;

    // `printf 'hello\n' | git hash-object --stdin`
    private const string HelloGitBlobSha1 = "ce013625030ba8dba906f756967f9e9ca394464a";

    private static readonly byte[] Hello = Encoding.ASCII.GetBytes("hello\n");

    [Fact]
    public async Task ResolveRevisionAsync_ParsesTheLiveFixture_AndReturnsTheCanonicalId()
    {
        var source = CreateSource(_ => Json(RevisionFixture), out var handler);

        var revision = await source.ResolveRevisionAsync("supertone/Supertonic-3", "main", TestContext.Current.CancellationToken);

        revision.Should().Be(new ResolvedRevision(RepositoryId, CommitSha, IsPrivate: false, IsGated: false));
        handler.Requests.Should().ContainSingle().Which.AbsoluteUri
            .Should().Be("https://huggingface.co/api/models/supertone/Supertonic-3/revision/main");
    }

    [Theory]
    [InlineData("\"private\":true,\"gated\":false", true, false)]
    [InlineData("\"private\":false,\"gated\":\"manual\"", false, true)]
    [InlineData("\"private\":false,\"gated\":\"auto\"", false, true)]
    [InlineData("\"private\":false,\"gated\":true", false, true)]
    public async Task ResolveRevisionAsync_ReadsPrivateAndGated(string flags, bool isPrivate, bool isGated)
    {
        var source = CreateSource(_ => Json($$"""{"id":"{{RepositoryId}}","sha":"{{CommitSha}}",{{flags}}}"""), out _);

        var revision = await source.ResolveRevisionAsync(RepositoryId, "main", TestContext.Current.CancellationToken);

        revision.IsPrivate.Should().Be(isPrivate);
        revision.IsGated.Should().Be(isGated);
    }

    [Theory]
    [InlineData("""{"id":"Supertone/supertonic-3"}""")]
    [InlineData("""{"id":"Supertone/supertonic-3","sha":"main"}""")]
    [InlineData("not json")]
    public async Task ResolveRevisionAsync_NoCommitSha_IsUnavailable(string body)
    {
        var source = CreateSource(_ => Json(body), out _);

        var act = () => source.ResolveRevisionAsync(RepositoryId, "main", TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ModelRepositorySourceException>()).Which.Kind.Should().Be(ModelRepositorySourceFailureKind.Unavailable);
    }

    [Fact]
    public async Task ResolveRevisionAsync_EscapesTheRevision()
    {
        var source = CreateSource(_ => Json(RevisionFixture), out var handler);

        await source.ResolveRevisionAsync(RepositoryId, "refs/pr/1", TestContext.Current.CancellationToken);

        handler.Requests.Should().ContainSingle().Which.AbsoluteUri.Should().EndWith("/revision/refs%2Fpr%2F1");
    }

    [Fact]
    public async Task ListFilesAsync_FollowsTheLinkCursor_AndKeepsOnlyFiles()
    {
        const string oid = "0000000000000000000000000000000000000001";
        const string lfsOid = "a3f5c1d2e4b6a7980f1e2d3c4b5a69788796a5b4c3d2e1f0a9b8c7d6e5f4a3b2";
        var nextUrl = $"https://huggingface.co/api/models/{RepositoryId}/tree/{CommitSha}?recursive=1&cursor=page2";
        var source = CreateSource(request =>
        {
            if (!request.RequestUri!.Query.Contains("cursor", StringComparison.Ordinal))
            {
                var first = Json($$"""
                    [{"type":"file","oid":"{{oid}}","size":12,"path":"config.json"},
                     {"type":"directory","oid":"{{oid}}","size":0,"path":"onnx"}]
                    """);
                first.Headers.Add("Link", $"<{nextUrl}>; rel=\"next\"");
                return first;
            }

            return Json($$$"""
                [{"type":"file","oid":"{{{oid}}}","size":134,"path":"onnx/model.onnx","lfs":{"oid":"{{{lfsOid}}}","size":250000000,"pointerSize":134}}]
                """);
        }, out var handler);

        var files = await source.ListFilesAsync(RepositoryId, CommitSha, TestContext.Current.CancellationToken);

        files.Should().Equal(
            new ModelRepositoryFile("config.json", 12, Sha256: null, GitBlobSha1: oid),
            new ModelRepositoryFile("onnx/model.onnx", 250_000_000, Sha256: lfsOid, GitBlobSha1: oid));
        handler.Requests.Select(u => u.AbsoluteUri).Should().Equal(
            $"https://huggingface.co/api/models/{RepositoryId}/tree/{CommitSha}?recursive=1",
            nextUrl);
    }

    [Fact]
    public async Task ListFilesAsync_IncompleteEntry_IsUnavailable()
    {
        var source = CreateSource(_ => Json("""[{"type":"file","path":"config.json"}]"""), out _);

        var act = () => source.ListFilesAsync(RepositoryId, CommitSha, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ModelRepositorySourceException>()).Which.Kind.Should().Be(ModelRepositorySourceFailureKind.Unavailable);
    }

    [Fact]
    public async Task ListFilesAsync_NextCursorOnAnotherHost_IsRefused()
    {
        var source = CreateSource(_ =>
        {
            var page = Json("[]");
            page.Headers.Add("Link", "<https://evil.example/next>; rel=\"next\"");
            return page;
        }, out var handler);

        var act = () => source.ListFilesAsync(RepositoryId, CommitSha, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ModelRepositorySourceException>()).Which.Kind.Should().Be(ModelRepositorySourceFailureKind.Unavailable);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task DownloadAsync_FollowsTheCdnRedirect_StreamsBytes_AndVerifiesSha256()
    {
        var bytes = RandomNumberGenerator.GetBytes(300_000);
        var file = new ModelRepositoryFile("onnx/model.onnx", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), "unused");
        const string cdn = "https://us.aws.cdn.hf.co/repos/aa/bb/model.onnx?X-Amz-Signature=abc";
        var source = CreateSource(request => request.RequestUri!.Host == "huggingface.co" ? Redirect(cdn) : Bytes(bytes), out var handler);
        var reports = new List<long>();
        using var destination = new MemoryStream();

        await source.DownloadAsync(RepositoryId, CommitSha, file, destination, new SyncProgress(reports), TestContext.Current.CancellationToken);

        destination.ToArray().Should().Equal(bytes);
        reports.Should().NotBeEmpty().And.BeInAscendingOrder();
        reports[^1].Should().Be(bytes.Length);
        handler.Requests.Select(u => u.AbsoluteUri).Should().Equal(
            $"https://huggingface.co/{RepositoryId}/resolve/{CommitSha}/onnx/model.onnx",
            cdn);
    }

    [Fact]
    public async Task DownloadAsync_SmallFile_IsVerifiedByGitBlobSha1()
    {
        var file = new ModelRepositoryFile("README.md", Hello.Length, Sha256: null, HelloGitBlobSha1);
        var source = CreateSource(_ => Bytes(Hello), out _);
        using var destination = new MemoryStream();

        await source.DownloadAsync(RepositoryId, CommitSha, file, destination, new SyncProgress([]), TestContext.Current.CancellationToken);

        destination.ToArray().Should().Equal(Hello);
    }

    [Fact]
    public async Task DownloadAsync_EscapesEachPathSegment()
    {
        var file = new ModelRepositoryFile("voice styles/ü#1.json", Hello.Length, Sha256: null, HelloGitBlobSha1);
        var source = CreateSource(_ => Bytes(Hello), out var handler);

        await source.DownloadAsync(RepositoryId, CommitSha, file, Stream.Null, new SyncProgress([]), TestContext.Current.CancellationToken);

        handler.Requests.Should().ContainSingle().Which.AbsoluteUri
            .Should().Be($"https://huggingface.co/{RepositoryId}/resolve/{CommitSha}/voice%20styles/%C3%BC%231.json");
    }

    [Theory]
    [InlineData("https://evil.example/model.onnx")]
    [InlineData("http://cdn-lfs.hf.co/model.onnx")]
    [InlineData("https://huggingface.co.evil.example/model.onnx")]
    [InlineData("https://cdn-lfs.hf.co:8443/model.onnx")]
    [InlineData("https://user@cdn-lfs.hf.co/model.onnx")]
    [InlineData("https://127.0.0.1/model.onnx")]
    public async Task DownloadAsync_RedirectOffTheAllowlist_IsRefusedBeforeItIsFetched(string target)
    {
        var source = CreateSource(request => request.RequestUri!.Host == "huggingface.co" ? Redirect(target) : Bytes(Hello), out var handler);

        var act = () => source.DownloadAsync(RepositoryId, CommitSha, SmallFile(), Stream.Null, new SyncProgress([]), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ModelRepositorySourceException>()).Which.Kind.Should().Be(ModelRepositorySourceFailureKind.Unavailable);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task DownloadAsync_SixthRedirect_IsRefused()
    {
        var hop = 0;
        var source = CreateSource(_ => Redirect($"https://cdn-lfs.hf.co/hop/{++hop}"), out var handler);

        var act = () => source.DownloadAsync(RepositoryId, CommitSha, SmallFile(), Stream.Null, new SyncProgress([]), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ModelRepositorySourceException>()).Which.Kind.Should().Be(ModelRepositorySourceFailureKind.Unavailable);
        handler.Requests.Should().HaveCount(HuggingFaceRedirectPolicy.MaxRedirects + 1);
    }

    [Theory]
    [InlineData("https://cdn-lfs-us-1.hf.co/repos/model.onnx")]
    [InlineData("https://cas-bridge.xethub.hf.co/xet-bridge-us/model.onnx")]
    [InlineData("https://cdn-lfs.huggingface.co/repos/model.onnx")]
    public async Task DownloadAsync_RedirectToHuggingFaceCdn_IsAllowed(string target)
    {
        var source = CreateSource(request => request.RequestUri!.Host == "huggingface.co" ? Redirect(target) : Bytes(Hello), out var handler);
        using var destination = new MemoryStream();

        await source.DownloadAsync(RepositoryId, CommitSha, SmallFile(), destination, new SyncProgress([]), TestContext.Current.CancellationToken);

        destination.ToArray().Should().Equal(Hello);
        handler.Requests.Select(u => u.AbsoluteUri).Should().EndWith(target);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ModelRepositorySourceFailureKind.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized, ModelRepositorySourceFailureKind.NotFound)]
    [InlineData(HttpStatusCode.Forbidden, ModelRepositorySourceFailureKind.GatedOrPrivate)]
    [InlineData(HttpStatusCode.BadRequest, ModelRepositorySourceFailureKind.Unavailable)]
    public async Task ResolveRevisionAsync_ErrorStatus_MapsToKind(HttpStatusCode status, ModelRepositorySourceFailureKind expected)
    {
        var source = CreateSource(_ => new HttpResponseMessage(status), out var handler);

        var act = () => source.ResolveRevisionAsync(RepositoryId, "main", TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ModelRepositorySourceException>()).Which.Kind.Should().Be(expected);
        handler.Requests.Should().ContainSingle("only 429 and 5xx are retried");
    }

    [Fact]
    public async Task ResolveRevisionAsync_429WithRetryAfter_WaitsThatLong_ThenSucceeds()
    {
        var time = new ImmediateTimeProvider();
        var attempts = 0;
        var source = CreateSource(_ =>
        {
            if (++attempts > 1)
            {
                return Json(RevisionFixture);
            }

            var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
            return throttled;
        }, out _, time);

        var revision = await source.ResolveRevisionAsync(RepositoryId, "main", TestContext.Current.CancellationToken);

        revision.CommitSha.Should().Be(CommitSha);
        attempts.Should().Be(2);
        time.RequestedDelays.Should().Equal(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ResolveRevisionAsync_RetryAfterIsCappedAtSixtySeconds()
    {
        var time = new ImmediateTimeProvider();
        var attempts = 0;
        var source = CreateSource(_ =>
        {
            if (++attempts > 1)
            {
                return Json(RevisionFixture);
            }

            var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
            return throttled;
        }, out _, time);

        await source.ResolveRevisionAsync(RepositoryId, "main", TestContext.Current.CancellationToken);

        time.RequestedDelays.Should().Equal(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task ResolveRevisionAsync_Persistent503_IsUnavailableAfterThreeRetries()
    {
        var time = new ImmediateTimeProvider();
        var source = CreateSource(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), out var handler, time);

        var act = () => source.ResolveRevisionAsync(RepositoryId, "main", TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ModelRepositorySourceException>()).Which.Kind.Should().Be(ModelRepositorySourceFailureKind.Unavailable);
        handler.Requests.Should().HaveCount(4);
        time.RequestedDelays.Should().Equal(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ResolveRevisionAsync_ConnectionFailure_IsUnavailable_WithTheCauseAsInner()
    {
        var failure = new HttpRequestException("No such host is known.");
        var source = CreateSource(_ => throw failure, out _);

        var act = () => source.ResolveRevisionAsync(RepositoryId, "main", TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<ModelRepositorySourceException>()).Which;
        thrown.Kind.Should().Be(ModelRepositorySourceFailureKind.Unavailable);
        thrown.InnerException.Should().BeSameAs(failure);
        thrown.Message.Should().NotContain("No such host");
    }

    [Fact]
    public async Task DownloadAsync_NoBytesWithinTheStallTimeout_IsDownloadStalled()
    {
        var time = new ImmediateTimeProvider();
        var source = CreateSource(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) }, out _, time);

        var act = () => source.DownloadAsync(RepositoryId, CommitSha, SmallFile(), Stream.Null, new SyncProgress([]), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ModelRepositorySourceException>()).Which.Kind.Should().Be(ModelRepositorySourceFailureKind.DownloadStalled);
        time.RequestedDelays.Should().Contain(TimeSpan.FromSeconds(new CustomModelsOptions().StallTimeoutSeconds));
    }

    [Fact]
    public async Task DownloadAsync_CallerCancels_IsNotReportedAsAStall()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var source = CreateSource(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) }, out _);

        var download = source.DownloadAsync(RepositoryId, CommitSha, SmallFile(), Stream.Null, new SyncProgress([]), cancellation.Token);
        await cancellation.CancelAsync();

        var act = async () => await download;

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DownloadAsync_Sha256Mismatch_IsIntegrityMismatch()
    {
        var file = new ModelRepositoryFile("onnx/model.onnx", Hello.Length, new string('0', 64), "unused");
        var source = CreateSource(_ => Bytes(Hello), out _);

        var act = () => source.DownloadAsync(RepositoryId, CommitSha, file, Stream.Null, new SyncProgress([]), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ModelRepositorySourceException>()).Which.Kind.Should().Be(ModelRepositorySourceFailureKind.IntegrityMismatch);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task DownloadAsync_SizeDiffersFromTheListing_IsIntegrityMismatch(int sizeDelta)
    {
        var file = new ModelRepositoryFile("README.md", Hello.Length + sizeDelta, Sha256: null, HelloGitBlobSha1);
        var source = CreateSource(_ => Bytes(Hello), out _);

        var act = () => source.DownloadAsync(RepositoryId, CommitSha, file, Stream.Null, new SyncProgress([]), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ModelRepositorySourceException>()).Which.Kind.Should().Be(ModelRepositorySourceFailureKind.IntegrityMismatch);
    }

    [Theory]
    [InlineData("https://huggingface.co/api/models", true)]
    [InlineData("https://HuggingFace.co/x", true)]
    [InlineData("https://cdn-lfs.hf.co/x", true)]
    [InlineData("https://hf.co/x", false)]
    [InlineData("https://evilhf.co/x", false)]
    [InlineData("https://huggingface.co:444/x", false)]
    [InlineData("http://huggingface.co/x", false)]
    [InlineData("https://a:b@huggingface.co/x", false)]
    [InlineData("https://[::1]/x", false)]
    public void RedirectPolicy_AllowsOnlyHuggingFaceOverDefaultHttps(string url, bool expected) =>
        HuggingFaceRedirectPolicy.IsAllowed(new Uri(url)).Should().Be(expected);

    private static HuggingFaceModelRepositorySource CreateSource(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        out RecordingHttpMessageHandler handler,
        TimeProvider? timeProvider = null)
    {
        handler = new RecordingHttpMessageHandler(responder);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(HuggingFaceModelRepositorySource.HttpClientName).Returns(new HttpClient(handler));
        var options = Substitute.For<IOptionsMonitor<CustomModelsOptions>>();
        options.CurrentValue.Returns(new CustomModelsOptions());

        return new HuggingFaceModelRepositorySource(
            factory,
            options,
            timeProvider ?? TimeProvider.System,
            NullLogger<HuggingFaceModelRepositorySource>.Instance);
    }

    private static ModelRepositoryFile SmallFile() => new("README.md", Hello.Length, Sha256: null, HelloGitBlobSha1);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Bytes(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location);
        return response;
    }

    /// <summary>Unlike <see cref="Progress{T}"/>, reports synchronously, so the list is complete when the call returns.</summary>
    private sealed class SyncProgress(List<long> reports) : IProgress<long>
    {
        public void Report(long value) => reports.Add(value);
    }
}
