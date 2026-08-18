using FluentValidation;

namespace AskLucy.Application.Memory.Commands.EditMemory;

public sealed class EditMemoryCommandValidator : AbstractValidator<EditMemoryCommand>
{
    public EditMemoryCommandValidator()
    {
        RuleFor(c => c.MemoryId).NotEmpty();
        RuleFor(c => c.Content).NotEmpty().MaximumLength(4000);
    }
}
