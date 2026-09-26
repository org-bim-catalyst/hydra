using System.Security.Claims;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Ai;
using AskLucy.Domain.OperationalFailures;
using AskLucy.Infrastructure.OperationalFailures;
using AskLucy.Web.Middleware;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Web.Tests.OperationalFailures;

/// <summary>
/// specs/074 T038 (FR-004, FR-013) — a chat failure lands on the admin trail exactly once, with the
/// classified kind, the user and chat it involved, and the correlation id the server log line
/// carries. Driven through the real <see cref="ChannelOperationalFailureRecorder"/> so the stamped
/// correlation id is the one <see cref="CorrelationIdAccessor"/> actually reads.
/// </summary>
public sealed class ChatFailureRecordingTests
{
    private const string TraceId = "trace-074";

    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly ChannelOperationalFailureRecorder _recorder;

    public ChatFailureRecordingTests()
    {
        _recorder = new ChannelOperationalFailureRecorder(
            Microsoft.Extensions.Options.Options.Create(new OperationalFailuresOptions()),
            new CorrelationIdAccessor(_httpContextAccessor),
            TimeProvider.System,
            NullLogger<ChannelOperationalFailureRecorder>.Instance);
    }

    [Fact]
    public async Task BeforeTheStream_ShouldRecordAChatFailure_WithTheUserAndCorrelationId()
    {
        var context = MiddlewareContext("/api/v1/ai/chat");

        await RunMiddlewareAsync(context, new AiProviderAuthenticationException("The key was rejected."));

        var report = Drain().Should().ContainSingle().Subject.Should().BeOfType<OperationalFailureReport>().Subject;
        report.Engine.Should().Be(OperationalFailureEngine.Chat);
        report.Kind.Should().Be(OperationalFailureKind.CredentialRejected);
        report.References.UserId.Should().Be("user-1");
        report.CorrelationId.Should().Be(TraceId);
    }

    [Fact]
    public async Task BeforeTheStream_ShouldRecordAnAiProviderFailure_OutsideTheChatRoutes()
    {
        await RunMiddlewareAsync(MiddlewareContext("/api/v1/admin/ai-providers/sync"), new AiProviderUnavailableException("Down."));

        Drain().Should().ContainSingle().Which.Should().BeOfType<OperationalFailureReport>()
            .Which.Engine.Should().Be(OperationalFailureEngine.AiProvider);
    }

    [Fact]
    public async Task BeforeTheStream_ShouldRecordNothing_ForAValidationFailure()
    {
        await RunMiddlewareAsync(MiddlewareContext("/api/v1/ai/chat"), new ValidationException([new ValidationFailure("Message", "Required.")]));

        Drain().Should().BeEmpty("a caller's own mistake is not an operational failure");
    }

    [Fact]
    public async Task BeforeTheStream_ShouldRecordNothing_WhenTheCallerCancelled()
    {
        var context = MiddlewareContext("/api/v1/ai/chat");
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        context.RequestAborted = aborted.Token;

        await RunMiddlewareAsync(context, new OperationCanceledException(aborted.Token));

        Drain().Should().BeEmpty();
    }

    [Fact]
    public async Task BeforeTheStream_ShouldRecordNothing_WhenTheSiteAlreadyRecordedIt()
    {
        var failure = new AiProviderUnavailableException("Down.");
        failure.MarkOperationalFailureRecorded();

        await RunMiddlewareAsync(MiddlewareContext("/api/v1/ai/chat"), failure);

        Drain().Should().BeEmpty("research D11 — the site that recorded and rethrew owns the record");
    }

    [Fact]
    public async Task MidStream_ShouldRecordTheClassifiedKind_ExactlyOnce()
    {
        using var harness = new UserFacingFailureTextTests.ChatHarness(_recorder);
        harness.HttpContext.Items[CorrelationIdKeys.ItemsKey] = TraceId;
        _httpContextAccessor.HttpContext.Returns(harness.HttpContext);

        await harness.RunTurnThrowingAsync(new AiProviderQuotaExhaustedException("Quota exhausted."), TestContext.Current.CancellationToken);

        var report = Drain().Should().ContainSingle().Subject.Should().BeOfType<OperationalFailureReport>().Subject;
        report.Engine.Should().Be(OperationalFailureEngine.Chat);
        report.Operation.Should().Be("Chat reply");
        report.Kind.Should().Be(OperationalFailureKind.QuotaExhausted);
        report.References.UserId.Should().Be("user-1");
        report.References.ChatId.Should().Be(harness.ChatId);
        report.CorrelationId.Should().Be(TraceId);
    }

    [Fact]
    public async Task MidStream_ShouldRecordAnUnexpectedError_ForAnUnclassifiedException()
    {
        using var harness = new UserFacingFailureTextTests.ChatHarness(_recorder);

        await harness.RunTurnThrowingAsync(new InvalidOperationException("boom"), TestContext.Current.CancellationToken);

        Drain().Should().ContainSingle().Which.Should().BeOfType<OperationalFailureReport>()
            .Which.Kind.Should().Be(OperationalFailureKind.UnexpectedError);
    }

    [Fact]
    public async Task MidStream_ShouldRecordNothing_WhenTheCallerCancelled()
    {
        using var harness = new UserFacingFailureTextTests.ChatHarness(_recorder);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        async IAsyncEnumerable<ChatStreamChunk> CancelledStream()
        {
            yield return new ChatStreamChunk("Looking that up", null);
            await cancellation.CancelAsync();
            throw new OperationCanceledException(cancellation.Token);
        }

        await harness.RunTurnAsync(CancelledStream(), cancellation.Token);

        Drain().Should().BeEmpty("the user left; nothing failed");
    }

    [Fact]
    public async Task MidStream_ShouldRecordNothing_WhenTheCallerCancelsWhileTheReplyIsStillWorking()
    {
        using var harness = new UserFacingFailureTextTests.ChatHarness(_recorder);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        // Cancelled while the next chunk is still being worked on, so the keep-alive timer - which
        // shares the token - is already cancelled when it races the pending chunk. The CI flake of
        // 2026-09-26: the timer won, and the turn was reported as failed.
        async IAsyncEnumerable<ChatStreamChunk> StillWorkingStream()
        {
            yield return new ChatStreamChunk("Looking that up", null);
            await cancellation.CancelAsync();
            await Task.Delay(50, CancellationToken.None);
            throw new OperationCanceledException(cancellation.Token);
        }

        await harness.RunTurnAsync(StillWorkingStream(), cancellation.Token);

        Drain().Should().BeEmpty("the user left; nothing failed");
    }

    private DefaultHttpContext MiddlewareContext(string path)
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        context.Request.Path = path;
        context.Request.Method = HttpMethods.Post;
        context.Items[CorrelationIdKeys.ItemsKey] = TraceId;
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test"));
        _httpContextAccessor.HttpContext.Returns(context);
        return context;
    }

    private Task RunMiddlewareAsync(HttpContext context, Exception failure) =>
        new ProblemDetailsMiddleware(
            _ => throw failure,
            NullLogger<ProblemDetailsMiddleware>.Instance,
            _recorder,
            new FailureClassifier()).InvokeAsync(context);

    private List<OperationalFailureSignal> Drain()
    {
        var signals = new List<OperationalFailureSignal>();
        while (_recorder.Reader.TryRead(out var signal))
        {
            signals.Add(signal);
        }

        return signals;
    }
}
