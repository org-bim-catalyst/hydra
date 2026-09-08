using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.Locations;
using AskLucy.Application.SiteBoundaries;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// Builds a <see cref="SendChatMessageCommandHandler"/> wired to a real
/// <see cref="ConversationTurnOrchestrator"/> over the caller's substitutes.
///
/// <para>
/// <b>Why a factory rather than updating each test to build the orchestrator directly.</b> The
/// specs/045 T007–T010 extraction moved the turn body out of the handler, which changed the
/// handler's constructor and broke nine test files that had built it by hand. The tempting fix is
/// to point those tests at the orchestrator instead — but most of them exist to characterise
/// behaviour <i>through the handler</i>, and a characterisation test that changes its entry point
/// during the refactor it is meant to guard has stopped guarding anything. Routing construction
/// through here keeps every test calling <c>_handler.Handle(...)</c> with the same substitutes and
/// the same assertions, so a behaviour change during the extraction still fails them.
/// </para>
///
/// <para>
/// The parameter list is deliberately the handler's <i>old</i> one, in the old order, so each call
/// site changed by exactly one token — the <c>new SendChatMessageCommandHandler(</c> call became
/// <c>SendChatMessageHandlerFactory.Create(</c> — and the diff stays reviewable as a mechanical
/// substitution rather than a rewrite.
/// </para>
/// </summary>
internal static class SendChatMessageHandlerFactory
{
    public static SendChatMessageCommandHandler Create(
        IAIProviderResolver resolver,
        IAIProviderRepository providers,
        IAIModelRepository models,
        IConversationKnowledgeBaseRepository conversationKnowledgeBases,
        IRagService ragService,
        IMemoryService memoryService,
        ILocationResolutionService locationResolutionService,
        IBoundaryResolutionService boundaryResolutionService,
        IViewerZoomDetector viewerZoomDetector,
        IUserChatRepository userChatRepository,
        ICurrentUserAccessor currentUser,
        IBackgroundJobClient backgroundJobClient,
        IOptions<LocationResolutionOptions> locationResolutionOptions,
        IOptions<BoundaryScoringOptions> boundaryScoringOptions,
        ILogger logger,
        SendChatMessageCommandValidator validator)
    {
        // The orchestrator owns every log message the handler used to emit, so a test that
        // asserts on logging must see its own logger arrive here. Substitutes are declared as
        // ILogger<SendChatMessageCommandHandler> in the existing fixtures; the orchestrator wants
        // ILogger<ConversationTurnOrchestrator>. Adapting rather than re-typing every fixture
        // keeps those assertions working against the same substitute instance.
        var orchestratorLogger = logger as ILogger<ConversationTurnOrchestrator>
            ?? new CategoryAdapter(logger);

        var orchestrator = new ConversationTurnOrchestrator(
            conversationKnowledgeBases,
            ragService,
            memoryService,
            locationResolutionService,
            boundaryResolutionService,
            viewerZoomDetector,
            userChatRepository,
            currentUser,
            backgroundJobClient,
            locationResolutionOptions,
            boundaryScoringOptions,
            orchestratorLogger);

        return new SendChatMessageCommandHandler(resolver, providers, models, orchestrator, validator);
    }

    /// <summary>
    /// Forwards every call to the test's own <see cref="ILogger"/> substitute, so
    /// <c>Received().Log(...)</c> assertions written against the handler's logger still observe
    /// the orchestrator's messages. Note that assertions using <c>[LoggerMessage]</c>-generated
    /// methods never match through NSubstitute anyway (a known trap in this repository); the
    /// working assertions go through <see cref="ILogger.Log"/> directly, which this preserves.
    /// </summary>
    private sealed class CategoryAdapter(ILogger inner) : ILogger<ConversationTurnOrchestrator>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
    }

    /// <summary>Convenience for fixtures that never assert on logging.</summary>
    public static ILogger NullLogger => NullLogger<ConversationTurnOrchestrator>.Instance;
}
