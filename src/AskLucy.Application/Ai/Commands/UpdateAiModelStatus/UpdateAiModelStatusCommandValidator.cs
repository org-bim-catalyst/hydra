using FluentValidation;

namespace AskLucy.Application.Ai.Commands.UpdateAiModelStatus;

public sealed class UpdateAiModelStatusCommandValidator : AbstractValidator<UpdateAiModelStatusCommand>
{
    public UpdateAiModelStatusCommandValidator()
    {
        RuleFor(c => c.ModelId).NotEmpty();
        RuleFor(c => c.Status).IsInEnum();
    }
}
