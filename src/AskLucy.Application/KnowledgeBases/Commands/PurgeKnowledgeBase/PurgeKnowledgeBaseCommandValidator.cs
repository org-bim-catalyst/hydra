using FluentValidation;

namespace AskLucy.Application.KnowledgeBases.Commands.PurgeKnowledgeBase;

/// <summary>Enforces explicit confirmation (FR-036) at the Application boundary, not only in the UI (constitution §2.VIII No Silent Failures) — mirrors <c>PurgeUserChatCommandValidator</c>.</summary>
public sealed class PurgeKnowledgeBaseCommandValidator : AbstractValidator<PurgeKnowledgeBaseCommand>
{
    public PurgeKnowledgeBaseCommandValidator() =>
        RuleFor(c => c.Confirm).Equal(true).WithMessage("Permanently deleting a knowledge base requires explicit confirmation.");
}
