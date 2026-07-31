using AskLucy.Application.Abstractions;
using FluentValidation;

namespace AskLucy.Application.Ai.Commands.SendChatMessage;

/// <summary>FR-015/FR-016: a provider/model/parameter combination that isn't valid is rejected here, naming the specific field, before any handler code runs.</summary>
public sealed class SendChatMessageCommandValidator : AbstractValidator<SendChatMessageCommand>
{
    public SendChatMessageCommandValidator(IAIProviderRepository providers, IAIModelRepository models)
    {
        RuleFor(c => c.Messages).NotEmpty().WithMessage("At least one message is required.");
        RuleForEach(c => c.Messages).ChildRules(message =>
        {
            message.RuleFor(m => m.Content).NotEmpty().WithMessage("Message content is required.");
        });

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
                    return;
                }

                var parameters = command.GenerationParameters;
                if (parameters is null)
                {
                    return;
                }

                if (parameters.JsonMode == true && !model.SupportsJsonMode)
                {
                    context.AddFailure("generationParameters.jsonMode", "The selected model does not support JSON mode.");
                }

                if (!string.IsNullOrEmpty(parameters.ReasoningLevel) && !model.SupportsReasoning)
                {
                    context.AddFailure("generationParameters.reasoningLevel", "The selected model does not support a reasoning level.");
                }
            });
    }
}
