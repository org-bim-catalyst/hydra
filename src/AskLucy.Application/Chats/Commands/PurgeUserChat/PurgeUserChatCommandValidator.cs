using FluentValidation;

namespace AskLucy.Application.Chats.Commands.PurgeUserChat;

/// <summary>Enforces explicit confirmation (FR-005) at the Application boundary, not only in the UI (constitution §2.VIII No Silent Failures).</summary>
public sealed class PurgeUserChatCommandValidator : AbstractValidator<PurgeUserChatCommand>
{
    public PurgeUserChatCommandValidator() =>
        RuleFor(c => c.Confirm).Equal(true).WithMessage("Permanently deleting a conversation requires explicit confirmation.");
}
