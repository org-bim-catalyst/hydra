using FluentValidation;

namespace AskLucy.Application.Prompts.Commands.CreatePrompt;

public sealed class CreatePromptCommandValidator : AbstractValidator<CreatePromptCommand>
{
    public CreatePromptCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).MaximumLength(1000);
        RuleFor(c => c.UserInstructions).NotEmpty();
        RuleFor(c => c.PreferredModelKey).MaximumLength(100);

        RuleForEach(c => c.Variables).ChildRules(variable =>
        {
            variable.RuleFor(v => v.Name).NotEmpty().Matches("^[A-Za-z_][A-Za-z0-9_]*$")
                .WithMessage("Variable names must start with a letter/underscore and contain only letters, digits, and underscores.");
        });

        RuleFor(c => c.Variables)
            .Must(variables => variables.Select(v => v.Name.ToLowerInvariant()).Distinct().Count() == variables.Count)
            .WithMessage("Variable names must be unique.");
    }
}
