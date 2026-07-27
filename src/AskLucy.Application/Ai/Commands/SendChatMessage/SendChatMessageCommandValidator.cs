using FluentValidation;

namespace AskLucy.Application.Ai.Commands.SendChatMessage;

public sealed class SendChatMessageCommandValidator : AbstractValidator<SendChatMessageCommand>
{
    public SendChatMessageCommandValidator()
    {
        RuleFor(c => c.Messages).NotEmpty().WithMessage("At least one message is required.");
        RuleForEach(c => c.Messages).ChildRules(message =>
        {
            message.RuleFor(m => m.Content).NotEmpty().WithMessage("Message content is required.");
        });
    }
}
