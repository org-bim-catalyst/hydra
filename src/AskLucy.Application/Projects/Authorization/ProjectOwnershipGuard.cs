using AskLucy.Domain.Projects;

namespace AskLucy.Application.Projects.Authorization;

/// <summary>Mirrors <c>ChatOwnershipGuard</c>/<c>MemoryOwnershipGuard</c> — denial looks like not-found (spec.md FR-002a).</summary>
public static class ProjectOwnershipGuard
{
    public static Project EnsureOwnedBy(Project? project, string userId)
    {
        if (project is null || !project.IsOwnedBy(userId))
        {
            throw new KeyNotFoundException("Project not found.");
        }

        return project;
    }
}
