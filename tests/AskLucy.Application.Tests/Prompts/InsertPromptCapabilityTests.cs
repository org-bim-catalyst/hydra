using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Chats.Commands.AppendMessage;
using AskLucy.Application.Prompts.Commands.InsertPromptIntoConversation;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Common;
using AskLucy.Domain.Prompts;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Prompts;

/// <summary>
/// tasks.md T096. A capability-incompatible conversation model must block insertion with a
/// specific warning before anything is sent (spec.md FR-004, User Story 5 AC3) — reuses
/// <see cref="PromptCapabilityChecker"/>, same as <c>ExecutePromptCommandHandler</c>.
/// </summary>
public sealed class InsertPromptCapabilityTests
{
    private const string OwnerId = "user-1";

    private readonly IPromptRepository _promptRepository = Substitute.For<IPromptRepository>();
    private readonly IUserChatRepository _userChatRepository = Substitute.For<IUserChatRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly IAIProviderRepository _providerRepository = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _modelRepository = Substitute.For<IAIModelRepository>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private InsertPromptIntoConversationCommandHandler CreateHandler() => new(
        _promptRepository, _userChatRepository, _messageRepository, _providerRepository, _modelRepository, _mediator, _currentUser);

    [Fact]
    public async Task Handle_ShouldThrowDomainRuleViolation_WhenTheConversationsModelLacksARequiredCapability()
    {
        var content = new PromptContentSnapshot(
            null, null, "Describe {{document}}.", null, null, null, null, null, null, null, null, false);
        var variables = new List<PromptVariableDefinition>
        {
            new("document", null, PromptVariableType.String, true, null, null, null, 0),
        };
        var (prompt, version) = Prompt.Create(
            OwnerId, $"Prompt {Guid.NewGuid():N}", null, PromptType.Chat, null, null,
            new PromptCapabilityRequirements(
                RequiresStreaming: false, RequiresVision: true, RequiresFunctionCalling: false, RequiresJsonMode: false,
                RequiresReasoning: false, RequiresEmbeddings: false, RequiresImageInput: false, RequiresImageOutput: false, RequiresAudio: false),
            null, content, variables, OwnerId);

        var provider = AIProvider.Create("openai", "OpenAI", "system");
        var model = AIModel.Create(
            provider.Id, "gpt-5", "GPT-5", 128000, 4096,
            new AIModelCapabilities(true, false, false, false, false, false, false, false, false), // vision = false
            null, null, "system");

        var chat = UserChat.Create("Conversation", OwnerId, null, OwnerId);
        chat.SetModelSelection(provider.Id, model.Id, null, OwnerId);

        _currentUser.UserId.Returns(OwnerId);
        _userChatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _promptRepository.GetByIdForOwnerAsync(prompt.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(prompt);
        _promptRepository.GetVersionAsync(prompt.Id, prompt.CurrentVersionNumber, Arg.Any<CancellationToken>()).Returns(version);
        _providerRepository.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);
        _modelRepository.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);

        var command = new InsertPromptIntoConversationCommand(chat.Id, prompt.Id, new Dictionary<string, string?> { ["document"] = "a diagram" });

        var act = async () =>
        {
            await foreach (var _ in CreateHandler().Handle(command, CancellationToken.None))
            {
            }
        };

        await act.Should().ThrowAsync<DomainRuleViolationException>();
        await _mediator.DidNotReceive().Send(Arg.Any<AppendMessageCommand>(), Arg.Any<CancellationToken>());
        _mediator.DidNotReceive().CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>());
    }
}
