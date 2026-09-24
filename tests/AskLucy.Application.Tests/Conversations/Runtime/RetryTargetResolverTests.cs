using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Conversations;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Conversations;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/068 T044 – T045 — the retry resolution rules from data-model.md §4, and the boundary that
/// keeps a probing client from learning anything.
///
/// <para>
/// Every case here answers the same question in a different shape: <b>what may the server replay,
/// and on whose say-so?</b> The resolver reads the capability, its arguments and its target from
/// the persisted outcome alone, so a request that could name those itself would be a general
/// capability-invocation endpoint rather than a retry (FR-010, constitution §5).
/// </para>
/// </summary>
public sealed class RetryTargetResolverTests
{
    private const string Owner = "user-1";
    private const string Arguments = """{"query":"Al Safa Park 2"}""";

    private readonly IMessageRepository _messages = Substitute.For<IMessageRepository>();
    private readonly IUserChatRepository _chats = Substitute.For<IUserChatRepository>();
    private readonly IConversationKnowledgeBaseRepository _knowledgeBases = Substitute.For<IConversationKnowledgeBaseRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly Guid _chatId = Guid.NewGuid();

    public RetryTargetResolverTests()
    {
        _currentUser.UserId.Returns(Owner);
        _knowledgeBases.GetByConversationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
    }

    // ---- fixture -----------------------------------------------------------

    private void ChatOwnedBy(string userId) =>
        _chats.GetByIdAsync(_chatId, Arg.Any<CancellationToken>())
            .Returns(UserChat.Create("A chat", userId, null, userId));

    private void Transcript(params Message[] messages) =>
        _messages.ListByChatIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(messages);

    private Message AssistantTurn(RecordedTurnOutcome? outcome, string? rawJson = null) =>
        Message.Create(
            _chatId, MessageRole.Assistant, MessageKind.Text, "Some reply.", null, Owner,
            turnOutcomeJson: rawJson ?? (outcome is null ? null : JsonSerializer.Serialize(outcome, RecordedTurnOutcomeJson.Options)));

    private static RecordedTurnOutcome Failed(string key, string target, string reason = "the provider rejected the request") =>
        RecordedTurnOutcome.Acted([ActionAttempt.Failure("Capability", key, target, Arguments, reason)], DateTimeOffset.UtcNow);

    private static RecordedTurnOutcome Succeeded(string key, string target) =>
        RecordedTurnOutcome.Acted([ActionAttempt.Success("Capability", key, target, Arguments)], DateTimeOffset.UtcNow);

    private RetryTargetResolver BuildResolver(params IConversationCapability[] capabilities)
    {
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions());
        var toolCatalog = new AgentToolCatalog([.. capabilities], new EmptyMcpToolRegistry());
        var indexRetriever = new CapabilityIndexRetriever(
            Substitute.For<IEmbeddingService>(), runtimeOptions, NullLogger<CapabilityIndexRetriever>.Instance);

        return new RetryTargetResolver(
            _messages, _chats, _knowledgeBases, _currentUser,
            new ConversationCapabilityCatalog(toolCatalog, indexRetriever, runtimeOptions));
    }

    private Task<RetryTarget> ResolveAsync(RetryTargetResolver resolver, Guid? failedMessageId = null) =>
        resolver.ResolveAsync(_chatId, failedMessageId, CancellationToken.None);

    // ---- T045: the boundary -------------------------------------------------

    [Fact]
    public async Task AChatBelongingToSomeoneElse_ShouldResolveAsNotFound()
    {
        ChatOwnedBy("someone-else");
        Transcript(AssistantTurn(Failed("stub", "Al Safa Park 2")));
        var resolver = BuildResolver(new StubCapability());

        var resolve = async () => await ResolveAsync(resolver, Guid.NewGuid());

        // Never a permission error: "you may not touch that" confirms the id exists, which is the
        // one fact a probing client is after. Not-found says nothing either way.
        var thrown = await resolve.Should().ThrowAsync<ConversationActionUnknownException>();
        thrown.Which.Message.Should().NotContainAny("permission", "forbidden", "not allowed", "access");
    }

    [Fact]
    public async Task AMessageIdFromAnotherChat_ShouldResolveAsNotFound()
    {
        ChatOwnedBy(Owner);
        Transcript(AssistantTurn(Failed("stub", "Al Safa Park 2")));
        var resolver = BuildResolver(new StubCapability());

        // The id is well-formed and may well exist somewhere; it is simply not in this
        // conversation, and the answer is the same as for an id that exists nowhere at all.
        var resolve = async () => await ResolveAsync(resolver, Guid.NewGuid());

        await resolve.Should().ThrowAsync<ConversationActionUnknownException>();
    }

    [Fact]
    public async Task AMissingChat_ShouldResolveAsNotFound()
    {
        _chats.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns((UserChat?)null);
        Transcript();

        var resolve = async () => await ResolveAsync(BuildResolver(new StubCapability()));

        await resolve.Should().ThrowAsync<ConversationActionUnknownException>();
    }

    // ---- T044: resolution rules --------------------------------------------

    [Fact]
    public async Task ARecordedFailure_ShouldResolveToItsOwnPersistedArguments()
    {
        ChatOwnedBy(Owner);
        var turn = AssistantTurn(Failed("stub", "Al Safa Park 2"));
        Transcript(turn);

        var target = await ResolveAsync(BuildResolver(new StubCapability()), turn.Id);

        // FR-010 — every parameter of the replay comes from the record, not the request.
        target.SourceMessageId.Should().Be(turn.Id);
        target.Attempt.Key.Should().Be("stub");
        target.Attempt.TargetLabel.Should().Be("Al Safa Park 2");
        target.Attempt.ArgumentsJson.Should().Be(Arguments);
    }

    [Fact]
    public async Task ATurnWithNoRecordedOutcome_ShouldResolveAsNotFound()
    {
        ChatOwnedBy(Owner);
        var turn = AssistantTurn(outcome: null);
        Transcript(turn);

        var resolve = async () => await ResolveAsync(BuildResolver(new StubCapability()), turn.Id);

        // Nothing was recorded, so there is nothing to replay. Guessing from the prose is exactly
        // the reading this whole feature removes.
        await resolve.Should().ThrowAsync<ConversationActionUnknownException>();
    }

    [Fact]
    public async Task ATurnThatSucceeded_ShouldBeRefusedRatherThanQuietlyRerun()
    {
        ChatOwnedBy(Owner);
        var turn = AssistantTurn(Succeeded("stub", "Al Safa Park 2"));
        Transcript(turn);

        var resolve = async () => await ResolveAsync(BuildResolver(new StubCapability()), turn.Id);

        // FR-015 — saying so is the answer; running it again behind the user's back is not.
        var thrown = await resolve.Should().ThrowAsync<ConversationActionStaleException>();
        thrown.Which.Message.Should().Contain("already succeeded");
    }

    [Fact]
    public async Task ATurnThatOnlyTalked_ShouldSayThereIsNothingToRetry()
    {
        ChatOwnedBy(Owner);
        var turn = AssistantTurn(RecordedTurnOutcome.AnsweredInWords(DateTimeOffset.UtcNow));
        Transcript(turn);

        var resolve = async () => await ResolveAsync(BuildResolver(new StubCapability()), turn.Id);

        var thrown = await resolve.Should().ThrowAsync<ConversationActionStaleException>();
        thrown.Which.Message.Should().Contain("nothing to retry");
    }

    [Fact]
    public async Task ATypedRetryWithNothingFailed_ShouldSaySo()
    {
        ChatOwnedBy(Owner);
        Transcript(AssistantTurn(Succeeded("stub", "Al Safa Park 2")));

        var resolve = async () => await ResolveAsync(BuildResolver(new StubCapability()));

        var thrown = await resolve.Should().ThrowAsync<ConversationActionUnknownException>();
        thrown.Which.Message.Should().Contain("nothing that failed");
    }

    [Fact]
    public async Task ATypedRetryWithTwoDifferentFailures_ShouldAskWhichOne()
    {
        ChatOwnedBy(Owner);
        Transcript(
            AssistantTurn(Failed("stub", "Al Safa Park 2")),
            AssistantTurn(Failed("other", "Dubai Marina")));

        var resolve = async () => await ResolveAsync(BuildResolver(new StubCapability(), new StubCapability("other")));

        // FR-012 — ambiguity is answered with a question. Picking the newest here would run one of
        // two plausible things with no way for the user to tell which.
        var thrown = await resolve.Should().ThrowAsync<ConversationActionUnknownException>();
        thrown.Which.Message.Should().Contain("which would you like");
    }

    [Fact]
    public async Task ATypedRetryWithTheSameFailureTwice_ShouldResolveToTheNewest()
    {
        ChatOwnedBy(Owner);
        var first = AssistantTurn(Failed("stub", "Al Safa Park 2"));
        var second = AssistantTurn(Failed("stub", "Al Safa Park 2"));
        Transcript(first, second);

        var target = await ResolveAsync(BuildResolver(new StubCapability()));

        // Two attempts at the same thing are not ambiguous: retrying either means the same work,
        // so asking would be pedantry rather than honesty.
        target.SourceMessageId.Should().Be(second.Id);
    }

    [Fact]
    public async Task ACapabilityThatIsNoLongerRegistered_ShouldResolveAsUnknown()
    {
        ChatOwnedBy(Owner);
        var turn = AssistantTurn(Failed("removed-capability", "Al Safa Park 2"));
        Transcript(turn);

        var resolve = async () => await ResolveAsync(BuildResolver(new StubCapability()), turn.Id);

        await resolve.Should().ThrowAsync<ConversationActionUnknownException>();
    }

    [Fact]
    public async Task ACapabilityNoLongerAvailableHere_ShouldResolveAsStale()
    {
        ChatOwnedBy(Owner);
        var turn = AssistantTurn(Failed("stub", "Al Safa Park 2"));
        Transcript(turn);

        var resolve = async () => await ResolveAsync(
            BuildResolver(new StubCapability(available: false)), turn.Id);

        // The changed-preconditions case: registered, but it cannot run in this conversation any
        // more. 409, and said plainly — replaying it would fail for a reason the user cannot act on.
        var thrown = await resolve.Should().ThrowAsync<ConversationActionStaleException>();
        thrown.Which.Message.Should().Contain("Al Safa Park 2");
    }

    [Fact]
    public async Task AnUnreadableRecordedOutcome_ShouldBeSkippedRatherThanCrashTheTurn()
    {
        ChatOwnedBy(Owner);
        var corrupt = AssistantTurn(outcome: null, rawJson: "{ not json at all");
        var usable = AssistantTurn(Failed("stub", "Al Safa Park 2"));
        Transcript(corrupt, usable);

        var target = await ResolveAsync(BuildResolver(new StubCapability()));

        // An outcome nobody can read is not a retryable one. The orchestrator logs this same
        // condition when it builds the routing summary, so it is never silent (§2 VIII).
        target.SourceMessageId.Should().Be(usable.Id);
    }

    // ---- fixture doubles ----------------------------------------------------

    private sealed class EmptyMcpToolRegistry : IMcpToolRegistry
    {
        public IReadOnlyCollection<IAgentTool> ActiveTools => [];

        public Task InvalidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubCapability(string name = "stub", bool available = true) : IConversationCapability
    {
        public string Name => name;

        public string Description => "A stub capability.";

        public string WhenToUse => "Use when a test needs a capability to exist.";

        public string ArgumentHint => "anything";

        public string UsageGuidance => "Report what came back.";

        public string Label => "Stub";

        public string OfferDescription => "A stub.";

        public string AcknowledgementTemplate => "OK, doing the thing.";

        public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

        public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

        public string InputSchemaJson => """{"type":"object"}""";

        public string OutputSchemaJson => """{"type":"object"}""";

        public CapabilityDuration ExpectedDuration => CapabilityDuration.Brief;

        public SubAgentArea Area => SubAgentArea.Location;

        public bool IsAvailable(TurnContext context) => available;

        public Task<AgentToolResult> ExecuteAsync(
            AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default) =>
            Task.FromResult(AgentToolResult.Success(JsonDocument.Parse("""{"status":"done"}""")));
    }
}
