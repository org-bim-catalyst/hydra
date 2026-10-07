using MediatR;

namespace AskLucy.Application.Localization.Commands.UpdateLocalizationSettings;

/// <summary>
/// contracts/admin-notifications-api.md PUT /localization (M): guarded by the row version the administrator last read.
/// 409 on a stale one, 422 when <c>en</c> is missing or a code isn't a platform language.
/// </summary>
public sealed record UpdateLocalizationSettingsCommand(bool IsEnabled, IReadOnlyList<string> SupportedLanguages, byte[] ExpectedRowVersion)
    : IRequest<AdminLocalizationDto>;
