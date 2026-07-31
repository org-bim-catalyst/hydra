using AskLucy.Application.Abstractions;
using FluentValidation;

namespace AskLucy.Application.Chats.Commands.UpdateChatModelSelection;

public sealed class UpdateChatModelSelectionCommandValidator : AbstractValidator<UpdateChatModelSelectionCommand>
{
    public UpdateChatModelSelectionCommandValidator(IAIProviderRepository providers, IAIModelRepository models)
    {
        RuleFor(c => c.ChatId).NotEmpty();
        RuleFor(c => c.ProviderId).NotEmpty();
        RuleFor(c => c.ModelId).NotEmpty();

        RuleFor(c => c)
            .CustomAsync(async (command, context, cancellationToken) =>
            {
                var provider = await providers.GetByIdAsync(command.ProviderId, cancellationToken);
                if (provider is null || !provider.IsEnabled)
                {
                    context.AddFailure("providerId", "The selected provider is not enabled.");
                    return;
                }

                var model = await models.GetByIdAsync(command.ModelId, cancellationToken);
                if (model is null || !model.IsSelectable || model.ProviderId != command.ProviderId)
                {
                    context.AddFailure("modelId", "The selected model is not available for the selected provider.");
                }
            });
    }
}
