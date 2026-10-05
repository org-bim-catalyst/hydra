using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Appearance.Queries.GetPresenceSphereSettings;

/// <summary>specs/080 contracts "GET": the stored settings, or the defaults while none have been saved.</summary>
public sealed record GetPresenceSphereSettingsQuery : IRequest<PresenceSphereSettingsDto>;

public sealed class GetPresenceSphereSettingsQueryHandler(
    IPresenceSphereSettingsRepository settings,
    IUserProfileRepository profiles) : IRequestHandler<GetPresenceSphereSettingsQuery, PresenceSphereSettingsDto>
{
    public async Task<PresenceSphereSettingsDto> Handle(GetPresenceSphereSettingsQuery request, CancellationToken cancellationToken)
    {
        var stored = await settings.GetAsync(cancellationToken);
        return stored is null
            ? PresenceSphereSettingsDto.Defaults
            : await PresenceSphereSettingsDto.FromAsync(stored, profiles, cancellationToken);
    }
}
