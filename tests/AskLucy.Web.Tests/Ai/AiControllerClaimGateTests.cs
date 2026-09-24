using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Chats;
using AskLucy.Application.Chats.Commands.AppendMessage;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Chats;
using AskLucy.Web.Contracts;
using AskLucy.Web.Controllers.v1;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Web.Tests.Ai;

/// <summary>
/// specs/068 T037 – T038 — the reported defect, reproduced end to end through the controller, and
/// the false-positive guard beside it.
///
/// <para>
/// The reproduction is two turns. The first dies on a provider failure and leaves a recorded
/// <see cref="TurnVerdict.FailedBeforeCompleting"/>; the second — the user's "try again" — comes
/// back from a restored provider claiming the work was already done. Before this feature that
/// second sentence reached the user verbatim, with nothing on screen to back it up.
/// </para>
/// </summary>
public sealed class AiControllerClaimGateTests : IDisposable
{
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly ISelectedActionResolver _selectedActionResolver = Substitute.For<ISelectedActionResolver>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly List<AppendMessageCommand> _appended = [];
    private readonly MemoryStream _responseBody = new();
    private readonly AiController _controller;
    private readonly Guid _chatId = Guid.NewGuid();

    public AiControllerClaimGateTests()
    {
        _currentUser.UserId.Returns("user-1");
        _mediator.Send(Arg.Any<AppendMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _appended.Add(call.Arg<AppendMessageCommand>()!);
                return new MessageDto(
                    Guid.NewGuid(), "assistant", "text", "Here you go.", null, DateTime.UtcNow,
                    null, null, null, null, null, null, null, null, null, [], []);
            });

        var turnRecorder = new TurnRecorder(
            Substitute.For<IAgentRepository>(), Substitute.For<IAgentExecutionRepository>(),
            Substitute.For<IUnitOfWork>(), NullLogger<TurnRecorder>.Instance);

        _controller = new AiController(_mediator, _providers, _models, _selectedActionResolver,
            Substitute.For<IRetryTargetResolver>(),
            Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions()),
            turnRecorder, _currentUser, NullLogger<AiController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Response = { Body = _responseBody } },
            },
        };
    }

    public void Dispose() => _responseBody.Dispose();

    /// <summary>The turn's own reply. Every turn also appends the user message, which is not it.</summary>
    private IEnumerable<AppendMessageCommand> AssistantMessages() =>
        _appended.Where(c => c.Role == MessageRole.Assistant);

    private string ResponseText() => Encoding.UTF8.GetString(_responseBody.ToArray());

    private Task RunTurnAsync(IAsyncEnumerable<ChatStreamChunk> stream, string userMessage)
    {
        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>()).Returns(stream);

        return _controller.Chat(
            new ChatRequest(_chatId, [new ChatMessageDto("user", userMessage)], Guid.NewGuid(), Guid.NewGuid(), null),
            CancellationToken.None);
    }

    private static async IAsyncEnumerable<ChatStreamChunk> FailingTurn()
    {
        yield return new ChatStreamChunk("Looking that up", null);
        await Task.Yield();
        throw new HttpRequestException("the provider credential is invalid");
    }

    private static async IAsyncEnumerable<ChatStreamChunk> RestoredTurnClaimingSuccess()
    {
        // The composer's own words on the retry, with a restored provider and no action taken: the
        // model read the previous turn's failure notice as an ordinary reply and concluded the work
        // was already done. Recorded honestly as AnsweredInWords, because it genuinely acted on
        // nothing.
        yield return new ChatStreamChunk("I've already shown you Al Safa Park 2 on the map.", null);
        yield return new ChatStreamChunk(null, null, TurnOutcome: RecordedTurnOutcome.AnsweredInWords(DateTimeOffset.UtcNow));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ARetryThatDidNothing_ShouldNotClaimTheWorkWasAlreadyDone()
    {
        await RunTurnAsync(FailingTurn(), "Show me Al Safa Park 2");
        _responseBody.SetLength(0);
        _appended.Clear();

        await RunTurnAsync(RestoredTurnClaimingSuccess(), "try again");
        var text = ResponseText();

        // The exact sentence the user reported. It never reaches the wire, and it never reaches
        // storage either — a claim that survives a reload was never withheld at all (T033).
        text.Should().NotContain("I've already shown you");
        text.Should().Contain("I can't confirm");
        AssistantMessages().Should().ContainSingle().Which.Content.Should().NotContain("I've already shown you");
    }

    [Fact]
    public async Task AGenuinelySuccessfulConfirmation_ShouldPassThroughUnchanged()
    {
        const string confirmation = "I've shown you Al Safa Park 2 on the map.";

        async IAsyncEnumerable<ChatStreamChunk> Stream()
        {
            yield return new ChatStreamChunk(confirmation, null);
            yield return new ChatStreamChunk(null, null, TurnOutcome: RecordedTurnOutcome.Acted(
                [ActionAttempt.Success("Capability", "resolve_location", "Al Safa Park 2", "{}")],
                DateTimeOffset.UtcNow));
            await Task.CompletedTask;
        }

        await RunTurnAsync(Stream(), "Show me Al Safa Park 2");

        // The false-positive guard (the spec's "verification disagrees with a correct reply" edge
        // case). A gate that degraded correct turns would be a worse product than the defect.
        ResponseText().Should().Contain(confirmation);
        AssistantMessages().Should().ContainSingle().Which.Content.Should().Be(confirmation);
    }

    [Fact]
    public async Task ClaimFreeProse_ShouldStreamExactlyAsBefore()
    {
        const string answer = "Al Safa Park 2 sits just south of Sheikh Zayed Road. It covers about 64 hectares.";

        async IAsyncEnumerable<ChatStreamChunk> Stream()
        {
            foreach (var word in answer.Split(' '))
            {
                yield return new ChatStreamChunk(word + " ", null);
            }

            yield return new ChatStreamChunk(null, null, TurnOutcome: RecordedTurnOutcome.AnsweredInWords(DateTimeOffset.UtcNow));
            await Task.CompletedTask;
        }

        await RunTurnAsync(Stream(), "Tell me about Al Safa Park 2");

        // SC-001b — an ordinary answer is untouched by the gate, whichever way the deltas happen to
        // be chopped up by the provider.
        AssistantMessages().Should().ContainSingle().Which.Content.Should().Be(answer + " ");
    }
}
