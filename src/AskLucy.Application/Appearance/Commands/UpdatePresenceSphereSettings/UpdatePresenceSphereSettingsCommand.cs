using AskLucy.Application.Abstractions;
using AskLucy.Domain.Appearance;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Appearance.Commands.UpdatePresenceSphereSettings;

/// <summary>specs/080 contracts "PUT": replaces all three settings.</summary>
public sealed record UpdatePresenceSphereSettingsCommand(decimal DotSizeMultiplier, int CardFillPercent, bool ZoomEnabled)
    : IRequest<PresenceSphereSettingsDto>;

public sealed class UpdatePresenceSphereSettingsCommandValidator : AbstractValidator<UpdatePresenceSphereSettingsCommand>
{
    public UpdatePresenceSphereSettingsCommandValidator()
    {
        RuleFor(c => c.DotSizeMultiplier)
            .Must(PresenceSphereSettings.IsValidDotSizeMultiplier)
            .WithMessage($"dotSizeMultiplier must be between {PresenceSphereSettings.MinDotSizeMultiplier} and {PresenceSphereSettings.MaxDotSizeMultiplier}.");

        RuleFor(c => c.CardFillPercent)
            .Must(PresenceSphereSettings.IsValidCardFillPercent)
            .WithMessage($"cardFillPercent must be between {PresenceSphereSettings.MinCardFillPercent} and {PresenceSphereSettings.MaxCardFillPercent}.");
    }
}

public sealed class UpdatePresenceSphereSettingsCommandHandler(
    IPresenceSphereSettingsRepository settings,
    IUserProfileRepository profiles,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    ILogger<UpdatePresenceSphereSettingsCommandHandler> logger)
    : IRequestHandler<UpdatePresenceSphereSettingsCommand, PresenceSphereSettingsDto>
{
    public async Task<PresenceSphereSettingsDto> Handle(UpdatePresenceSphereSettingsCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var stored = await settings.GetAsync(cancellationToken);
        var before = stored is null
            ? PresenceSphereSettingsDto.Defaults
            : new PresenceSphereSettingsDto(stored.DotSizeMultiplier, stored.CardFillPercent, stored.ZoomEnabled, null, null, false);

        if (stored is null)
        {
            stored = PresenceSphereSettings.Create(request.DotSizeMultiplier, request.CardFillPercent, request.ZoomEnabled, actorUserId, utcNow);
            settings.Add(stored);
        }
        else
        {
            stored.Update(request.DotSizeMultiplier, request.CardFillPercent, request.ZoomEnabled, actorUserId, utcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        PresenceSphereSettingsLog.Changed(
            logger, actorUserId,
            before.DotSizeMultiplier, before.CardFillPercent, before.ZoomEnabled,
            stored.DotSizeMultiplier, stored.CardFillPercent, stored.ZoomEnabled);

        return await PresenceSphereSettingsDto.FromAsync(stored, profiles, cancellationToken);
    }
}

internal static partial class PresenceSphereSettingsLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Presence sphere settings changed by {ActorUserId}: dot size {OldDotSize} -> {NewDotSize}, fill {OldFill}% -> {NewFill}%, zoom {OldZoom} -> {NewZoom}")]
    private static partial void ChangedCore(
        ILogger logger, string actorUserId,
        decimal oldDotSize, decimal newDotSize, int oldFill, int newFill, bool oldZoom, bool newZoom);

    public static void Changed(
        ILogger logger, string actorUserId,
        decimal oldDotSize, int oldFill, bool oldZoom,
        decimal newDotSize, int newFill, bool newZoom) =>
        ChangedCore(logger, actorUserId, oldDotSize, newDotSize, oldFill, newFill, oldZoom, newZoom);
}
