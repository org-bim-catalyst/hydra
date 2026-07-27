using FluentValidation;

namespace AskLucy.Application.Chats.Commands.RenameUserChat;

public sealed class RenameUserChatCommandValidator : AbstractValidator<RenameUserChatCommand>
{
    public RenameUserChatCommandValidator()
    {
        RuleFor(c => c.ChatId).NotEmpty();
        RuleFor(c => c.NewTitle).NotEmpty().MaximumLength(200);
    }
}
