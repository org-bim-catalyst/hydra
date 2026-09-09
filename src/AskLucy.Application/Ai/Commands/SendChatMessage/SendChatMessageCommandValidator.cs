using System.Text.Json;
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

        // specs/045 US3 (T069) — a purely structural check. By the time this command exists,
        // AiController has already resolved the selection through ISelectedActionResolver (which
        // is what actually enforces staleness/ownership/availability, surfacing its own typed
        // 409/400 Problem Details); SelectedAction here is always server-constructed from that
        // resolution, never bound directly from the client's request body. This rule exists as
        // defense in depth against a malformed command reaching the handler at all, same spirit
        // as the provider/model checks above.
        When(c => c.SelectedAction is not null, () =>
        {
            RuleFor(c => c.SelectedAction!.OfferedByMessageId).NotEmpty();
            RuleFor(c => c.SelectedAction!.ArgumentsJson)
                .Must(BeAJsonObject)
                .WithMessage("selectedAction.arguments must be a JSON object.");
        });

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

    private static bool BeAJsonObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
