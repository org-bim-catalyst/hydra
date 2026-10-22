using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.ToolSettings;
using AskLucy.Domain.Agents;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Agents.Commands.UpdateToolSettings;

/// <summary>
/// specs/083 — saves some or all of one tool's settings. Keys not named keep their current
/// value; every named key must be one the tool declares, with a value of its type.
/// </summary>
public sealed record UpdateToolSettingsCommand(string Tool, IReadOnlyDictionary<string, string> Values) : IRequest;

public sealed class UpdateToolSettingsCommandValidator : AbstractValidator<UpdateToolSettingsCommand>
{
    public UpdateToolSettingsCommandValidator(ToolSettingCatalog catalog)
    {
        RuleFor(c => c.Tool).NotEmpty().WithMessage("Name a tool.");
        RuleFor(c => c.Values).NotEmpty().WithMessage("Name at least one setting to change.");

        RuleForEach(c => c.Values).Custom((entry, context) =>
        {
            var definition = catalog.Find(context.InstanceToValidate.Tool, entry.Key);
            if (definition is null)
            {
                context.AddFailure(entry.Key, $"{context.InstanceToValidate.Tool} has no setting '{entry.Key}'.");
            }
            else if (!ToolSettingCatalog.IsValidValue(definition, entry.Value))
            {
                context.AddFailure(entry.Key, $"'{definition.Label}' does not accept '{entry.Value}'.");
            }
        });
    }
}

public sealed class UpdateToolSettingsCommandHandler(
    IToolSettingRepository settings,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    ILogger<UpdateToolSettingsCommandHandler> logger) : IRequestHandler<UpdateToolSettingsCommand>
{
    public async Task Handle(UpdateToolSettingsCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var existing = (await settings.ListByToolAsync(request.Tool, cancellationToken))
            .ToDictionary(s => s.Key, StringComparer.Ordinal);

        foreach (var (key, value) in request.Values)
        {
            if (existing.TryGetValue(key, out var row))
            {
                row.Change(value, actorUserId);
            }
            else
            {
                settings.Add(ToolSetting.Create(request.Tool, key, value, actorUserId));
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            var detail = $"{request.Tool}: " + string.Join(", ", request.Values.Select(v => $"{v.Key}={v.Value}"));
            ToolSettingsLog.SettingsUpdated(logger, request.Tool, actorUserId, detail);
        }
    }
}

internal static partial class ToolSettingsLog
{
    [LoggerMessage(LogLevel.Information, Message = "Tool settings updated for {Tool} by {Actor}: {Detail}")]
    public static partial void SettingsUpdated(ILogger logger, string tool, string actor, string detail);
}
