using FluentValidation;

namespace AskLucy.Application.Chats.Commands.CreateUserChat;

public sealed class CreateUserChatCommandValidator : AbstractValidator<CreateUserChatCommand>
{
    public CreateUserChatCommandValidator() => RuleFor(c => c.Title).NotEmpty().MaximumLength(200);
}
