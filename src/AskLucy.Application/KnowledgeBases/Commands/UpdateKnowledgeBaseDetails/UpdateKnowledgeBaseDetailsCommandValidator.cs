using FluentValidation;

namespace AskLucy.Application.KnowledgeBases.Commands.UpdateKnowledgeBaseDetails;

public sealed class UpdateKnowledgeBaseDetailsCommandValidator : AbstractValidator<UpdateKnowledgeBaseDetailsCommand>
{
    public UpdateKnowledgeBaseDetailsCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).MaximumLength(2000);
        RuleFor(c => c.Color).MaximumLength(7);
        RuleFor(c => c.Icon).MaximumLength(50);
        RuleFor(c => c.Notes).MaximumLength(4000);
        RuleForEach(c => c.Tags).NotEmpty().MaximumLength(50);
    }
}
