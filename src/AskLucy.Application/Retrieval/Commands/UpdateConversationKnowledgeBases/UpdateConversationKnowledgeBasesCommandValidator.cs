using FluentValidation;

namespace AskLucy.Application.Retrieval.Commands.UpdateConversationKnowledgeBases;

public sealed class UpdateConversationKnowledgeBasesCommandValidator : AbstractValidator<UpdateConversationKnowledgeBasesCommand>
{
    public UpdateConversationKnowledgeBasesCommandValidator()
    {
        RuleFor(c => c.ChatId).NotEmpty();
        RuleFor(c => c.KnowledgeBaseIds).NotNull();
    }
}
