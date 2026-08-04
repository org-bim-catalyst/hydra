using FluentValidation;

namespace AskLucy.Application.KnowledgeBases.Commands.CreateKnowledgeBase;

public sealed class CreateKnowledgeBaseCommandValidator : AbstractValidator<CreateKnowledgeBaseCommand>
{
    public CreateKnowledgeBaseCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).MaximumLength(2000);
        RuleFor(c => c.Color).MaximumLength(7);
        RuleFor(c => c.Icon).MaximumLength(50);
        RuleForEach(c => c.Tags).NotEmpty().MaximumLength(50);
    }
}
