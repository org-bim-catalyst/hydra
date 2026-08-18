using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Chats;
using AskLucy.Application.Chats.Commands.AppendMessage;
using AskLucy.Application.Prompts.Commands.InsertPromptIntoConversation;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Prompts;
using FluentAssertions;
using FluentValidation;
using MediatR;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Prompts;

/// <summary>
/// tasks.md T095. <see cref="InsertPromptIntoConversationCommandHandler"/> must block on a
/// missing/invalid required variable (spec.md FR-013, User Story 5 AC1) before ever delegating to
/// <see cref="SendChatMessageCommand"/> or persisting anything — same "iterator throws before any
/// yield" contract already proven by <c>ExecutePromptCommandHandlerTests</c>.
/// </summary>
public sealed class InsertPromptIntoConversationTests
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

    private static (Prompt Prompt, PromptVersion Version) BuildPrompt()
    {
        var content = new PromptContentSnapshot(
            null, null, "Summarize {{document}}.", null, null, null, null, null, null, null, null, false);
        var variables = new List<PromptVariableDefinition>
        {
            new("document", null, PromptVariableType.String, true, null, null, null, 0),
        };
        return Prompt.Create(
            OwnerId, $"Prompt {Guid.NewGuid():N}", null, PromptType.Chat, null, null,
            PromptCapabilityRequirements.None, null, content, variables, OwnerId);
    }

    private static AIProvider BuildProvider() => AIProvider.Create("openai", "OpenAI", "system");

    private static AIModel BuildModel(Guid providerId) => AIModel.Create(
        providerId, "gpt-5", "GPT-5", 128000, 4096,
        new AIModelCapabilities(true, false, false, false, false, false, false, false, false), null, null, "system");

    [Fact]
    public async Task Handle_ShouldBlockBeforeAnyDelegation_WhenARequiredVariableIsMissing()
    {
        var (prompt, version) = BuildPrompt();
        var provider = BuildProvider();
        var model = BuildModel(provider.Id);
        var chat = UserChat.Create("Conversation", OwnerId, null, OwnerId);
        chat.SetModelSelection(provider.Id, model.Id, null, OwnerId);

        _currentUser.UserId.Returns(OwnerId);
        _userChatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _promptRepository.GetByIdForOwnerAsync(prompt.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(prompt);
        _promptRepository.GetVersionAsync(prompt.Id, prompt.CurrentVersionNumber, Arg.Any<CancellationToken>()).Returns(version);
        _providerRepository.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);
        _modelRepository.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);

        var command = new InsertPromptIntoConversationCommand(chat.Id, prompt.Id, new Dictionary<string, string?>());

        var act = async () =>
        {
            await foreach (var _ in CreateHandler().Handle(command, CancellationToken.None))
            {
            }
        };

        await act.Should().ThrowAsync<ValidationException>();
        await _mediator.DidNotReceive().Send(Arg.Any<AppendMessageCommand>(), Arg.Any<CancellationToken>());
        _mediator.DidNotReceive().CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldBlockBeforeAnyDelegation_WhenTheConversationHasNoProviderOrModelSelected()
    {
        var (prompt, version) = BuildPrompt();
        var chat = UserChat.Create("Conversation", OwnerId, null, OwnerId);

        _currentUser.UserId.Returns(OwnerId);
        _userChatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);
        _promptRepository.GetByIdForOwnerAsync(prompt.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(prompt);
        _promptRepository.GetVersionAsync(prompt.Id, prompt.CurrentVersionNumber, Arg.Any<CancellationToken>()).Returns(version);

        var command = new InsertPromptIntoConversationCommand(chat.Id, prompt.Id, new Dictionary<string, string?> { ["document"] = "text" });

        var act = async () =>
        {
            await foreach (var _ in CreateHandler().Handle(command, CancellationToken.None))
            {
            }
        };

        await act.Should().ThrowAsync<AskLucy.Domain.Common.DomainRuleViolationException>();
        await _mediator.DidNotReceive().Send(Arg.Any<AppendMessageCommand>(), Arg.Any<CancellationToken>());
    }
}
