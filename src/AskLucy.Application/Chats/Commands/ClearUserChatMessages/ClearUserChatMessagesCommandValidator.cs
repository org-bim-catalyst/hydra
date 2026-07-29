using FluentValidation;

namespace AskLucy.Application.Chats.Commands.ClearUserChatMessages;

/// <summary>Enforces explicit confirmation (FR-011) at the Application boundary, not only in the UI (constitution §2.VIII No Silent Failures).</summary>
public sealed class ClearUserChatMessagesCommandValidator : AbstractValidator<ClearUserChatMessagesCommand>
{
    public ClearUserChatMessagesCommandValidator() =>
        RuleFor(c => c.Confirm).Equal(true).WithMessage("Clearing a conversation's messages requires explicit confirmation.");
}
