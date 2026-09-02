using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Memory;
using AskLucy.Application.Tests.Ai;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Memory;
using AskLucy.Domain.Retrieval;
using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Memory;

/// <summary>
/// tasks.md T101 (FR-006b, quickstart.md Scenario 7) — <see cref="MemoryExtractionJob"/>'s
/// automatic retry-with-backoff and team-observable (never user-facing) failure handling. Written
/// here in <c>AskLucy.Application.Tests</c>, not <c>AskLucy.Infrastructure.Tests</c> as tasks.md
/// originally specified — <c>MemoryExtractionJob</c> itself lives in <c>AskLucy.Application</c>
/// (see its own doc comment, a deviation discovered and recorded during the Foundational phase of
/// <c>/speckit-implement</c>).
/// </summary>
public sealed class MemoryExtractionRetryTests
{
    [Fact]
    public void RunAsync_ShouldBeDecoratedWithAutomaticRetry_ThreeAttemptsWithExponentialBackoff()
    {
        var attribute = typeof(MemoryExtractionJob).GetCustomAttributes(typeof(AutomaticRetryAttribute), inherit: false)
            .Cast<AutomaticRetryAttribute>()
            .SingleOrDefault();

        attribute.Should().NotBeNull("FR-006b requires Hangfire to retry a failed extraction run automatically");
        attribute!.Attempts.Should().Be(3);
        attribute.DelaysInSeconds.Should().Equal(30, 120, 600);
    }

    [Fact]
    public async Task RunAsync_ShouldPropagateAProviderFailure_RatherThanSwallowingIt_SoHangfireCanRetry()
    {
        var userChatRepository = Substitute.For<IUserChatRepository>();
        var messageRepository = Substitute.For<IMessageRepository>();
        var aiProviderRepository = Substitute.For<IAIProviderRepository>();
        var aiModelRepository = Substitute.For<IAIModelRepository>();
        var aiProviderResolver = Substitute.For<IAIProviderResolver>();
        var aiProvider = Substitute.For<IAIProvider>();

        var provider = AIProvider.Create("openai", "OpenAI", "test");
        provider.SetCredential("ciphertext", "test");
        provider.Enable("test");
        var model = AIModel.Create(
            provider.Id, "gpt-4.1", "GPT-4.1", 128000, 16384,
            new AIModelCapabilities(true, true, true, true, false, false, true, false, false), null, null, "test");
        aiProviderRepository.ListEnabledAsync(Arg.Any<CancellationToken>()).Returns(new List<AIProvider> { provider });
        aiProviderRepository.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);
        aiModelRepository.ListAvailableByProviderIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(new List<AIModel> { model });
        aiModelRepository.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);
        aiProviderResolver.Resolve("openai").Returns(aiProvider);

        var chat = UserChat.Create("Test chat", "user-1", null, "user-1");
        userChatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        var message = Message.Create(chat.Id, MessageRole.User, MessageKind.Text, "I use PostgreSQL.", null, "user-1");
        messageRepository.ListByChatIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(new List<Message> { message });

        // Simulates a provider outage (e.g. AiProviderUnavailableException) — this must propagate,
        // not be caught and treated as "no candidates found" (that fallback is reserved
        // exclusively for a malformed/unparseable JSON response, a different failure mode entirely).
        aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), "gpt-4.1", Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChatCompletionResult>>(_ => throw new AiProviderUnavailableException("Provider is down."));

        var preferenceRepository = Substitute.For<IMemoryPreferenceRepository>();
        var job = new MemoryExtractionJob(
            userChatRepository, messageRepository, Substitute.For<IMemoryRepository>(), preferenceRepository,
            Substitute.For<IMemoryApprovalRepository>(), Substitute.For<IMemoryAuditLogRepository>(),
            Substitute.For<IMemoryEmbeddingRepository>(), Substitute.For<IMemoryConflictDetectionService>(),
            Substitute.For<IMemoryNotifier>(), Substitute.For<IEmbeddingProviderRepository>(),
            Substitute.For<IEmbeddingServiceResolver>(), Substitute.For<IMemoryVectorStore>(),
            aiProviderRepository, aiModelRepository, aiProviderResolver,
            CapabilityResolverTestFactory.Unassigned(aiProviderRepository, aiModelRepository),
            Substitute.For<IUnitOfWork>(), Substitute.For<ILogger<MemoryExtractionJob>>());

        var act = () => job.RunAsync(chat.Id, CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderUnavailableException>(
            "a real provider outage must reach Hangfire's [AutomaticRetry] rather than being silently absorbed as 'nothing found'");
    }
}
