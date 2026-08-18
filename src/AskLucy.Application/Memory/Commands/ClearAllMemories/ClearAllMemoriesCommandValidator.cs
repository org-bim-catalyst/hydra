using FluentValidation;

namespace AskLucy.Application.Memory.Commands.ClearAllMemories;

/// <summary>Enforces explicit confirmation (FR-023) at the Application boundary, not only in the UI (constitution §2.VIII No Silent Failures).</summary>
public sealed class ClearAllMemoriesCommandValidator : AbstractValidator<ClearAllMemoriesCommand>
{
    public ClearAllMemoriesCommandValidator() =>
        RuleFor(c => c.Confirm).Equal(true).WithMessage("Clearing all memories requires explicit confirmation.");
}
