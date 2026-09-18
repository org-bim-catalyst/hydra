using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
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
                    if (command.ModelId is not null)
                    {
                        context.AddFailure("modelId", "A model can only be pinned together with its provider.");
                    }

                    return; // Clearing is always allowed — it restores the platform default.
                }

                var provider = await providers.GetByIdAsync(providerId, cancellationToken);
                if (provider is null || !provider.IsEnabled)
                {
                    context.AddFailure("providerId", "The selected provider is not enabled.");
                    return;
                }

                if (command.ModelId is { } pinnedModelId)
                {
                    var pinned = await models.GetByIdAsync(pinnedModelId, cancellationToken);
                    if (pinned is null || pinned.ProviderId != providerId)
                    {
                        context.AddFailure("modelId", "The selected model does not belong to the selected provider.");
                    }
                    else if (!pinned.IsSelectable)
                    {
                        context.AddFailure("modelId", "The selected model is not Available.");
                    }
                    else if (command.Capability == AiCapability.ImageGeneration && !pinned.SupportsImageOutput)
                    {
                        context.AddFailure("modelId", "Image generation needs a model that can produce images.");
                    }

                    return; // A pinned model replaces the provider default, so the default's state is irrelevant.
                }

                if (command.Capability == AiCapability.ImageGeneration)
                {
                    context.AddFailure("modelId", "Choose an image-capable model — a provider's default model is a chat model.");
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
