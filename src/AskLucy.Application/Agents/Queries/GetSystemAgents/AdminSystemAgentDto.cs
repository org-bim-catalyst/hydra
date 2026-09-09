using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Queries.GetSystemAgents;

/// <summary>specs/047 FR-002 — one row of the admin System Agents view.</summary>
public sealed record AdminSystemAgentDto(
    Guid Id,
    string Name,
    string? SystemKey,
    AgentStatus Status,
    int? PublishedVersionNumber,
    DateTime LastUpdatedAtUtc)
{
    /// <summary>
    /// <see cref="LastUpdatedAtUtc"/> prefers the newest <see cref="AgentVersion.CreatedAtUtc"/> —
    /// the moment <c>SystemAgentProvisioner</c> last published a version, the precise operational
    /// signal FR-002 asks for — falling back to <see cref="Agent"/>'s own audit timestamps only for
    /// the edge case of a system agent record that has never been published (data-model.md).
    /// </summary>
    public static AdminSystemAgentDto FromEntity(Agent agent)
    {
        var newestVersion = agent.Versions
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();

        return new AdminSystemAgentDto(
            agent.Id,
            agent.Name,
            agent.SystemKey,
            agent.Status,
            agent.PublishedVersionNumber,
            newestVersion?.CreatedAtUtc ?? agent.ModifiedAtUtc ?? agent.CreatedAtUtc);
    }
}
