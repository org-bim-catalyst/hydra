using FluentValidation;

namespace AskLucy.Application.Agents.Commands.PublishAgentVersion;

public sealed class PublishAgentVersionCommandValidator : AbstractValidator<PublishAgentVersionCommand>
{
    public PublishAgentVersionCommandValidator()
    {
        RuleFor(c => c.ChangeDescription).MaximumLength(500);
    }
}
