using FluentValidation;

namespace AskLucy.Application.Ai.Commands.GenerateImage;

public sealed class GenerateImageCommandValidator : AbstractValidator<GenerateImageCommand>
{
    public GenerateImageCommandValidator() => RuleFor(c => c.Prompt).NotEmpty().MaximumLength(4000);
}
