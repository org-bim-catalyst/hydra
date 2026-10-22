using AskLucy.Domain.Agents;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// The stored values of the per-tool settings <see cref="Agents.ToolSettings.ToolSettingCatalog"/>
/// declares.
/// </summary>
public interface IToolSettingRepository
{
    Task<IReadOnlyList<ToolSetting>> ListAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ToolSetting>> ListByToolAsync(string toolName, CancellationToken cancellationToken = default);

    Task<ToolSetting?> GetAsync(string toolName, string key, CancellationToken cancellationToken = default);

    void Add(ToolSetting setting);
}
