using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.CapabilitySettings;
using AskLucy.Domain.Ai;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai.Commands.UpdateAiCapabilitySettings;

/// <summary>
/// specs/077 — saves some or all of one capability's settings. Keys not named keep their current
/// value; every named key must be one the capability declares, with a value of its type.
/// </summary>
public sealed record UpdateAiCapabilitySettingsCommand(AiCapability Capability, IReadOnlyDictionary<string, string> Values) : IRequest;

public sealed class UpdateAiCapabilitySettingsCommandValidator : AbstractValidator<UpdateAiCapabilitySettingsCommand>
{
    public UpdateAiCapabilitySettingsCommandValidator(CapabilitySettingCatalog catalog)
    {
        RuleFor(c => c.Capability).IsInEnum();
        RuleFor(c => c.Values).NotEmpty().WithMessage("Name at least one setting to change.");

        RuleForEach(c => c.Values).Custom((entry, context) =>
        {
            var definition = catalog.Find(context.InstanceToValidate.Capability, entry.Key);
            if (definition is null)
            {
                context.AddFailure(entry.Key, $"{context.InstanceToValidate.Capability} has no setting '{entry.Key}'.");
            }
            else if (!CapabilitySettingCatalog.IsValidValue(definition, entry.Value))
            {
                context.AddFailure(entry.Key, $"'{definition.Label}' must be true or false.");
            }
        });
    }
}

public sealed class UpdateAiCapabilitySettingsCommandHandler(
    IAiCapabilitySettingRepository settings,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    ILogger<UpdateAiCapabilitySettingsCommandHandler> logger) : IRequestHandler<UpdateAiCapabilitySettingsCommand>
{
    public async Task Handle(UpdateAiCapabilitySettingsCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var existing = (await settings.ListByCapabilityAsync(request.Capability, cancellationToken))
            .ToDictionary(s => s.Key, StringComparer.Ordinal);

        foreach (var (key, value) in request.Values)
        {
            if (existing.TryGetValue(key, out var row))
            {
                row.Change(value, actorUserId);
            }
            else
            {
                settings.Add(AiCapabilitySetting.Create(request.Capability, key, value, actorUserId));
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            var detail = $"{request.Capability}: " + string.Join(", ", request.Values.Select(v => $"{v.Key}={v.Value}"));
            AiAdminActionLog.AdminAiProviderActionPerformed(logger, "UpdateCapabilitySettings", actorUserId, Guid.Empty, detail);
        }
    }
}
