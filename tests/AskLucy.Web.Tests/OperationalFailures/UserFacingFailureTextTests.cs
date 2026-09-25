using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Chats;
using AskLucy.Application.Chats.Commands.AppendMessage;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.OperationalFailures;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Ai;
using AskLucy.Domain.OperationalFailures;
using AskLucy.Web.Contracts;
using AskLucy.Web.Controllers.v1;
using AskLucy.Web.Middleware;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Web.Tests.OperationalFailures;

/// <summary>
/// specs/074 T037 (FR-002, FR-003, SC-001) — for every provider failure kind, a non-administrator
/// sees one of the two calm sentences on every surface: the Problem Details before the stream, the
/// notice and <c>__TURN_OUTCOME__</c> reason mid-stream, and the persisted outcome the next turn's
/// <see cref="RecentTurnOutcomeSummary"/> is built from. An administrator keeps the classified detail.
/// </summary>
public sealed class UserFacingFailureTextTests
{
    /// <summary>SC-001: no credential, key, quota, rate limit, billing condition, provider, model or "an administrator".</summary>
    private static readonly string[] Sc001Words =
        ["credential", "key", "quota", "rate limit", "rate-limit", "billing", "provider", "model", "administrator"];

    public static TheoryData<AiProviderFailureKind> Kinds() => [.. Enum.GetValues<AiProviderFailureKind>()];

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ProblemDetails_ShouldBeACalmSentence_ForANonAdministrator(AiProviderFailureKind kind)
    {
        var body = await InvokeMiddlewareAsync(Failure(kind), asAdministrator: false);

        var detail = body.GetProperty("detail").GetString();
        detail.Should().BeOneOf(UserFacingFailureText.Retry, UserFacingFailureText.Later);
        AssertCalm(detail);
        body.TryGetProperty("providerFailure", out _).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task ProblemDetails_ShouldKeepTheClassifiedDetail_ForAnAdministrator(AiProviderFailureKind kind)
    {
        var failure = Failure(kind);

        var body = await InvokeMiddlewareAsync(failure, asAdministrator: true);

        body.GetProperty("detail").GetString().Should().Be(failure.Message);
        body.GetProperty("providerFailure").GetProperty("kind").GetString().Should().Be(kind.ToString());
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task MidStreamFailure_ShouldShowOnlyTheCalmSentence_OnTheWireAndInTheNextTurnsContext(AiProviderFailureKind kind)
    {
        var harness = new ChatHarness();

        var wire = await harness.RunTurnThrowingAsync(Failure(kind));

        var expected = UserFacingFailureText.For(OperationalFailureKinds.FromProvider(kind));
        var notice = wire.Split("\n\n").Select(l => l.StartsWith("data: ", StringComparison.Ordinal) ? l[6..] : l)
            .Where(l => l.Length > 0 && !l.StartsWith("__", StringComparison.Ordinal) && l != "[DONE]")
            .Last();
        notice.Trim().Should().Be(expected);
        AssertCalm(notice);

        var outcomeJson = wire.Split("__TURN_OUTCOME__")[1].Split("\n\n")[0];
        var wireReason = FailureReason(outcomeJson);
        wireReason.Should().Be(expected);
        AssertCalm(wireReason);

        // RecentTurnOutcomeSummary is built from this persisted outcome on the next turn.
        var persisted = harness.Appended.Should().ContainSingle(c => c.TurnOutcomeJson != null).Subject;
        var persistedReason = FailureReason(persisted.TurnOutcomeJson!);
        persistedReason.Should().Be(expected);
        AssertCalm(persisted.Content);
    }

    private static void AssertCalm(string? text)
    {
        foreach (var word in Sc001Words)
        {
            text.Should().NotContainEquivalentOf(word);
        }
    }

    private static string? FailureReason(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .Single(p => string.Equals(p.Name, "failureReason", StringComparison.OrdinalIgnoreCase)).Value.GetString();
    }

    /// <summary>Classifier prose that deliberately names every cause a user must not see.</summary>
    internal static AiProviderException Failure(AiProviderFailureKind kind)
    {
        const string message = "The ElevenLabs provider rejected the credential: quota, key, model and billing all named here.";
        return kind switch
        {
            AiProviderFailureKind.CredentialRejected => new AiProviderAuthenticationException(message),
            AiProviderFailureKind.CredentialUnreadable => new AiProviderCredentialUnreadableException(message),
            AiProviderFailureKind.NotConfigured => new AiProviderNotConfiguredException(message),
            AiProviderFailureKind.QuotaExhausted => new AiProviderQuotaExhaustedException(message),
            AiProviderFailureKind.RateLimited => new AiProviderRateLimitedException(message),
            AiProviderFailureKind.UsageRestricted => new AiProviderUsageRestrictedException(message),
            AiProviderFailureKind.Unavailable => new AiProviderUnavailableException(message),
            AiProviderFailureKind.RequestInvalid => new AiProviderRequestInvalidException(message),
            AiProviderFailureKind.ResponseNotUnderstood => new AiProviderResponseInvalidException(message),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Add the new kind here."),
        };
    }

    private static async Task<JsonElement> InvokeMiddlewareAsync(Exception exception, bool asAdministrator)
    {
        var middleware = new ProblemDetailsMiddleware(
            _ => throw exception,
            NullLogger<ProblemDetailsMiddleware>.Instance,
            Substitute.For<IOperationalFailureRecorder>(),
            new FailureClassifier());

        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        context.Request.Path = "/api/v1/ai/translate";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            asAdministrator ? [new Claim(ClaimTypes.Role, "Administrator")] : [new Claim(ClaimTypes.NameIdentifier, "user-1")],
            "test"));

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>The controller over a fake <see cref="ISender"/>, as <c>AiControllerTurnOutcomeStreamTests</c> drives it.</summary>
    internal sealed class ChatHarness
    {
        private readonly ISender _mediator = Substitute.For<ISender>();
        private readonly MemoryStream _responseBody = new();

        public ChatHarness(IOperationalFailureRecorder? recorder = null)
        {
            Recorder = recorder ?? Substitute.For<IOperationalFailureRecorder>();
            CurrentUser.UserId.Returns("user-1");
            _mediator.Send(Arg.Any<AppendMessageCommand>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Appended.Add(call.Arg<AppendMessageCommand>()!);
                    return new MessageDto(
                        Guid.NewGuid(), "assistant", "text", "Here you go.", null, DateTime.UtcNow,
                        null, null, null, null, null, null, null, null, null, [], []);
                });

            var turnRecorder = new TurnRecorder(
                Substitute.For<IAgentRepository>(), Substitute.For<IAgentExecutionRepository>(),
                Substitute.For<IUnitOfWork>(), NullLogger<TurnRecorder>.Instance);

            HttpContext = new DefaultHttpContext { Response = { Body = _responseBody } };
            Controller = new AiController(_mediator, Substitute.For<IAIProviderRepository>(), Substitute.For<IAIModelRepository>(),
                Substitute.For<ISelectedActionResolver>(), Substitute.For<IRetryTargetResolver>(),
                Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions()),
                turnRecorder, CurrentUser, Recorder, new FailureClassifier(), NullLogger<AiController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = HttpContext },
            };
        }

        public Guid ChatId { get; } = Guid.NewGuid();

        public DefaultHttpContext HttpContext { get; }

        public ICurrentUserAccessor CurrentUser { get; } = Substitute.For<ICurrentUserAccessor>();

        public IOperationalFailureRecorder Recorder { get; }

        public AiController Controller { get; }

        public List<AppendMessageCommand> Appended { get; } = [];

        public Task<string> RunTurnThrowingAsync(Exception failure, CancellationToken cancellationToken = default) =>
            RunTurnAsync(ThrowingStream(failure), cancellationToken);

        public async Task<string> RunTurnAsync(IAsyncEnumerable<ChatStreamChunk> stream, CancellationToken cancellationToken = default)
        {
            _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>()).Returns(stream);

            await Controller.Chat(
                new ChatRequest(ChatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], Guid.NewGuid(), Guid.NewGuid(), null),
                cancellationToken);

            return Encoding.UTF8.GetString(_responseBody.ToArray());
        }

        private static async IAsyncEnumerable<ChatStreamChunk> ThrowingStream(Exception failure)
        {
            yield return new ChatStreamChunk("Looking that up", null);
            await Task.Yield();
            throw failure;
        }
    }
}
