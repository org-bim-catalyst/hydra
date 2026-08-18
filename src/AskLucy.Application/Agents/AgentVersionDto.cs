using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents;

public sealed record AgentVersionDto(
    Guid Id,
    Guid AgentId,
    int VersionNumber,
    AgentInstructionsDto Instructions,
    Guid ModelProviderId,
    Guid ModelId,
    AgentExecutionPolicyDto ExecutionPolicy,
    string OutputFormat,
    string ToolsSnapshotJson,
    string KnowledgeBasesSnapshotJson,
    string? MemoryPolicySnapshotJson,
    string? ChangeDescription,
    string CreatedBy,
    DateTime CreatedAtUtc)
{
    public static AgentVersionDto Create(AgentVersion version) => new(
        version.Id, version.AgentId, version.VersionNumber, AgentInstructionsDto.FromDomain(version.Instructions),
        version.ModelProviderId, version.ModelId, AgentExecutionPolicyDto.FromDomain(version.ExecutionPolicy),
        version.OutputFormat.ToString(), version.ToolsSnapshotJson, version.KnowledgeBasesSnapshotJson,
        version.MemoryPolicySnapshotJson, version.ChangeDescription, version.CreatedBy, version.CreatedAtUtc);
}
