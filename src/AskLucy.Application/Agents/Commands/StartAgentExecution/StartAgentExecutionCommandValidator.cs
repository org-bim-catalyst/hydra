using AskLucy.Domain.Agents;
using FluentValidation;

namespace AskLucy.Application.Agents.Commands.StartAgentExecution;

public sealed class StartAgentExecutionCommandValidator : AbstractValidator<StartAgentExecutionCommand>
{
    public StartAgentExecutionCommandValidator()
    {
        RuleFor(c => c.Objective).NotEmpty().MaximumLength(10_000);
        RuleFor(c => c.AgentVersionNumber).GreaterThan(0).When(c => c.AgentVersionNumber is not null);

        RuleFor(c => c.UserChatId)
            .NotNull()
            .When(c => c.ConversationIntegrationMode == AgentConversationIntegrationMode.ExistingConversation)
            .WithMessage("An existing conversation id is required when conversationIntegrationMode is ExistingConversation.");
    }
}
