using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.CapabilitySettings;
using AskLucy.Domain.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetAiCapabilitySettings;

/// <summary>specs/077 — every capability with the settings it declares and their current values. A capability that declares none comes back with an empty list, which is what disables its gear.</summary>
public sealed record GetAiCapabilitySettingsQuery : IRequest<IReadOnlyList<AiCapabilitySettingsDto>>;

public sealed record AiCapabilitySettingsDto(AiCapability Capability, IReadOnlyList<AiCapabilitySettingDto> Settings);

/// <param name="Value">The saved value, or <paramref name="DefaultValue"/> when none is saved — always the value that applies now.</param>
public sealed record AiCapabilitySettingDto(
    string Key,
    CapabilitySettingValueType ValueType,
    string Label,
    string Description,
    string Value,
    string DefaultValue);

public sealed class GetAiCapabilitySettingsQueryHandler(
    IAiCapabilitySettingRepository settings, CapabilitySettingCatalog catalog)
    : IRequestHandler<GetAiCapabilitySettingsQuery, IReadOnlyList<AiCapabilitySettingsDto>>
{
    public async Task<IReadOnlyList<AiCapabilitySettingsDto>> Handle(
        GetAiCapabilitySettingsQuery request, CancellationToken cancellationToken)
    {
        var stored = (await settings.ListAllAsync(cancellationToken))
            .ToDictionary(s => (s.Capability, s.Key));

        return [.. Enum.GetValues<AiCapability>().Select(capability => new AiCapabilitySettingsDto(
            capability,
            [.. catalog.For(capability).Select(definition =>
            {
                var value = stored.TryGetValue((capability, definition.Key), out var row)
                    && CapabilitySettingCatalog.IsValidValue(definition, row.Value)
                        ? row.Value
                        : definition.DefaultValue;
                return new AiCapabilitySettingDto(
                    definition.Key, definition.ValueType, definition.Label, definition.Description, value, definition.DefaultValue);
            })]))];
    }
}
