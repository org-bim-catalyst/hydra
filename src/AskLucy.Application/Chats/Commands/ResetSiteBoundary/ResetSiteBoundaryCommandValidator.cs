using FluentValidation;

namespace AskLucy.Application.Chats.Commands.ResetSiteBoundary;

public sealed class ResetSiteBoundaryCommandValidator : AbstractValidator<ResetSiteBoundaryCommand>
{
    public ResetSiteBoundaryCommandValidator()
    {
        RuleFor(c => c.ChatId).NotEmpty();
        RuleFor(c => c.ExpectedRevision)
            .Must(revision => Guid.TryParse(revision, out _))
            .WithMessage("The revision must be a valid identifier.");
    }
}
