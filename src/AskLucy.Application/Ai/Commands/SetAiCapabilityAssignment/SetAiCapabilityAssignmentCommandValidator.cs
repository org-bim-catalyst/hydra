using AskLucy.Application.Abstractions;
using FluentValidation;

namespace AskLucy.Application.Ai.Commands.SetAiCapabilityAssignment;

public sealed class SetAiCapabilityAssignmentCommandValidator : AbstractValidator<SetAiCapabilityAssignmentCommand>
{
    public SetAiCapabilityAssignmentCommandValidator(IAIProviderRepository providers, IAIModelRepository models)
    {
        RuleFor(c => c.Capability).IsInEnum();

        RuleFor(c => c)
            .CustomAsync(async (command, context, cancellationToken) =>
            {
                if (command.ProviderId is not { } providerId)
                {
                    return; // Clearing is always allowed — it restores the platform default.
                }

                var provider = await providers.GetByIdAsync(providerId, cancellationToken);
                if (provider is null || !provider.IsEnabled)
                {
                    context.AddFailure("providerId", "The selected provider is not enabled.");
                    return;
                }

                // Assigning a provider with no usable default model would store a setting that
                // silently does nothing: AiCapabilityProviderResolver would log the assignment as
                // unusable and fall back to the platform default. Rejected here so the
                // administrator finds out at the moment they choose, not from a log days later.
                if (provider.DefaultModelId is not { } defaultModelId)
                {
                    context.AddFailure("providerId", "Set a default model for this provider before assigning it to a capability.");
                    return;
                }

                var model = await models.GetByIdAsync(defaultModelId, cancellationToken);
                if (model is not { IsSelectable: true })
                {
                    context.AddFailure("providerId", "This provider's default model is no longer Available — set a different one first.");
                }
            });
    }
}
