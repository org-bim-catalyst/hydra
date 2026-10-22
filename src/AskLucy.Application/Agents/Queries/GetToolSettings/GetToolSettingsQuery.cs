using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.ToolSettings;
using MediatR;

namespace AskLucy.Application.Agents.Queries.GetToolSettings;

/// <summary>
/// specs/083 — every tool that declares settings, with the values that apply now. A tool that
/// declares none is absent from the list, which is what leaves its gear disabled.
/// </summary>
public sealed record GetToolSettingsQuery : IRequest<IReadOnlyList<ToolSettingsDto>>;

public sealed record ToolSettingsDto(string Tool, IReadOnlyList<ToolSettingDto> Settings);

/// <param name="Value">The saved value, or <paramref name="DefaultValue"/> when none is saved — always the value that applies now.</param>
/// <param name="ScopedBy">
/// The key of the setting whose value narrows this one's options, and the value of that setting
/// this one applies to. Both null when the setting always applies.
/// </param>
public sealed record ToolSettingDto(
    string Key,
    ToolSettingValueType ValueType,
    string Label,
    string Description,
    string Value,
    string DefaultValue,
    IReadOnlyList<ToolSettingOptionDto> Options,
    int? Minimum,
    int? Maximum,
    string? ScopedBy = null,
    string? ScopeValue = null);

public sealed record ToolSettingOptionDto(string Value, string Label, string? Scope = null);

public sealed class GetToolSettingsQueryHandler(
    IToolSettingRepository settings, ToolSettingCatalog catalog)
    : IRequestHandler<GetToolSettingsQuery, IReadOnlyList<ToolSettingsDto>>
{
    public async Task<IReadOnlyList<ToolSettingsDto>> Handle(
        GetToolSettingsQuery request, CancellationToken cancellationToken)
    {
        var stored = (await settings.ListAllAsync(cancellationToken))
            .ToDictionary(s => (s.ToolName, s.Key));

        return [.. catalog.ConfigurableTools.Select(tool => new ToolSettingsDto(
            tool,
            [.. catalog.For(tool).Select(definition =>
            {
                var value = stored.TryGetValue((tool, definition.Key), out var row)
                    && ToolSettingCatalog.IsValidValue(definition, row.Value)
                        ? row.Value
                        : definition.DefaultValue;
                return new ToolSettingDto(
                    definition.Key,
                    definition.ValueType,
                    definition.Label,
                    definition.Description,
                    value,
                    definition.DefaultValue,
                    [.. definition.Choices.Select(o => new ToolSettingOptionDto(o.Value, o.Label, o.Scope))],
                    definition.Minimum,
                    definition.Maximum,
                    definition.ScopedBy,
                    definition.ScopeValue);
            })]))];
    }
}
