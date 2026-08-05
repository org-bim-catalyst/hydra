using FluentValidation;

namespace AskLucy.Application.Documents.Commands.RenameDocument;

public sealed class RenameDocumentCommandValidator : AbstractValidator<RenameDocumentCommand>
{
    public RenameDocumentCommandValidator() => RuleFor(c => c.FileName).NotEmpty().MaximumLength(260);
}
