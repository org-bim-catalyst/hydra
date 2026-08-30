using AskLucy.Application.Abstractions;
using FluentValidation;

namespace AskLucy.Application.Ai.Commands.UpdateAiProvider;

public sealed class UpdateAiProviderCommandValidator : AbstractValidator<UpdateAiProviderCommand>
{
    public UpdateAiProviderCommandValidator(IAIModelRepository models)
    {
        RuleFor(c => c.ProviderId).NotEmpty();

        // A platform default naming a model from another provider, or one that is not
        // Available, is silently useless: DefaultProviderResolver requires IsSelectable and
        // would skip straight past it to the next provider in display-name order — which is
        // exactly the alphabetical accident this setting exists to end. Rejected at the
        // boundary instead, mirroring SaveUserAiPreferenceCommandValidator.
        RuleFor(c => c)
            .CustomAsync(async (command, context, cancellationToken) =>
            {
                if (command.ClearDefaultModel || command.DefaultModelId is not { } modelId)
                {
                    return;
                }

                var model = await models.GetByIdAsync(modelId, cancellationToken);
                if (model is null || model.ProviderId != command.ProviderId)
                {
                    context.AddFailure("defaultModelId", "The selected model does not belong to this provider.");
                    return;
                }

                if (!model.IsSelectable)
                {
                    context.AddFailure("defaultModelId", "Only an Available model can be the platform default.");
                }
            });
    }
}
