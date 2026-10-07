using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Common;
using AskLucy.Domain.Localization;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Localization.Commands.UpdateLocalizationSettings;

public sealed class UpdateLocalizationSettingsCommandHandler(
    ILocalizationSettingRepository settings,
    ILocalizationSettingsProvider provider,
    INotificationAuditWriter audit,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateLocalizationSettingsCommand, AdminLocalizationDto>
{
    public async Task<AdminLocalizationDto> Handle(UpdateLocalizationSettingsCommand request, CancellationToken cancellationToken)
    {
        var setting = await settings.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("The localization setting row is missing; the migration seeds it.");

        if (!setting.RowVersion.AsSpan().SequenceEqual(request.ExpectedRowVersion))
        {
            throw new ConcurrencyConflictException("The localization settings were changed by someone else. Reload them and try again.");
        }

        settings.ExpectRowVersion(setting, request.ExpectedRowVersion);
        var before = new { isEnabled = setting.IsEnabled, supportedLanguages = setting.SupportedLanguages };

        try
        {
            setting.Update(request.IsEnabled, request.SupportedLanguages);
        }
        catch (DomainRuleViolationException ex)
        {
            throw new LocalizationRejectedException(ex.Message);
        }

        audit.Write(
            NotificationAuditAction.LocalizationSettingChanged,
            "LocalizationSetting",
            setting.Id.ToString(),
            NotificationAuditOutcome.Succeeded,
            new { before, after = new { isEnabled = setting.IsEnabled, supportedLanguages = setting.SupportedLanguages } });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Only after the save: an evicted cache that is refilled before the commit would serve the old state for another 30 s.
        provider.Evict();
        return LocalizationMapper.ToAdminDto(setting);
    }
}
