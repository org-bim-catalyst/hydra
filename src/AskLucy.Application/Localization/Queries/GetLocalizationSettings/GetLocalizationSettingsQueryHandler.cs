using AskLucy.Domain.Localization;
using MediatR;

namespace AskLucy.Application.Localization.Queries.GetLocalizationSettings;

public sealed class GetLocalizationSettingsQueryHandler(ILocalizationSettingRepository settings)
    : IRequestHandler<GetLocalizationSettingsQuery, AdminLocalizationDto>
{
    public async Task<AdminLocalizationDto> Handle(GetLocalizationSettingsQuery request, CancellationToken cancellationToken)
    {
        var setting = await settings.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("The localization setting row is missing; the migration seeds it.");
        return LocalizationMapper.ToAdminDto(setting);
    }
}
