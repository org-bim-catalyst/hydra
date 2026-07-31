using AskLucy.Application.Abstractions;
using FluentValidation;

namespace AskLucy.Application.Ai.Commands.SaveUserAiPreference;

/// <summary>contracts/preferences.md: 400 if `defaultModelId` doesn't belong to `defaultProviderId`, or `defaultProviderId` isn't enabled.</summary>
public sealed class SaveUserAiPreferenceCommandValidator : AbstractValidator<SaveUserAiPreferenceCommand>
{
    public SaveUserAiPreferenceCommandValidator(IAIProviderRepository providers, IAIModelRepository models)
    {
        RuleFor(c => c.DefaultProviderId).NotEmpty();
        RuleFor(c => c.DefaultModelId).NotEmpty();

        RuleFor(c => c)
            .CustomAsync(async (command, context, cancellationToken) =>
            {
                var provider = await providers.GetByIdAsync(command.DefaultProviderId, cancellationToken);
                if (provider is null || !provider.IsEnabled)
                {
                    context.AddFailure("defaultProviderId", "The selected provider is not enabled.");
                    return;
                }

                var model = await models.GetByIdAsync(command.DefaultModelId, cancellationToken);
                if (model is null || !model.IsSelectable || model.ProviderId != command.DefaultProviderId)
                {
                    context.AddFailure("defaultModelId", "The selected model is not available for the selected provider.");
                }
            });
    }
}
