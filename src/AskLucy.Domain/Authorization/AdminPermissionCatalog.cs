using System.Collections.ObjectModel;

namespace AskLucy.Domain.Authorization;

/// <summary>
/// Read-only catalogue of admin-panel permissions. Never mutated after construction.
/// </summary>
public static class AdminPermissionCatalog
{
    public static IReadOnlyCollection<AdminPermission> All { get; }

    private static readonly List<AdminPermission> _allList = new(18);

    static AdminPermissionCatalog()
    {
        // Dashboard — view-only
        _allList.Add(Permission("admin.dashboard.view", AdminArea.Dashboard, AdminPermissionLevel.View, "View dashboard", "View the admin dashboard overview."));

        // Users
        _allList.Add(Permission("admin.users.view", AdminArea.Users, AdminPermissionLevel.View, "View users", "View user list and details."));
        _allList.Add(Permission("admin.users.manage", AdminArea.Users, AdminPermissionLevel.Manage, "Manage users", "Create, edit, suspend, or delete users."));

        // AI providers
        _allList.Add(Permission("admin.ai-providers.view", AdminArea.AiProviders, AdminPermissionLevel.View, "View AI providers", "View registered AI provider configurations."));
        _allList.Add(Permission("admin.ai-providers.manage", AdminArea.AiProviders, AdminPermissionLevel.Manage, "Manage AI providers", "Add, edit, or remove AI provider credentials."));

        // Default models
        _allList.Add(Permission("admin.default-models.view", AdminArea.DefaultModels, AdminPermissionLevel.View, "View default models", "View the admin's chosen default model per provider."));
        _allList.Add(Permission("admin.default-models.manage", AdminArea.DefaultModels, AdminPermissionLevel.Manage, "Manage default models", "Change the admin's default model for any provider."));

        // AI capabilities
        _allList.Add(Permission("admin.ai-capabilities.view", AdminArea.AiCapabilities, AdminPermissionLevel.View, "View AI capabilities", "View which AI features are enabled system-wide."));
        _allList.Add(Permission("admin.ai-capabilities.manage", AdminArea.AiCapabilities, AdminPermissionLevel.Manage, "Manage AI capabilities", "Enable or disable AI features such as chat, images, voice."));

        // Agent policies
        _allList.Add(Permission("admin.agent-policies.view", AdminArea.AgentPolicies, AdminPermissionLevel.View, "View agent policies", "View agent execution policy configurations."));
        _allList.Add(Permission("admin.agent-policies.manage", AdminArea.AgentPolicies, AdminPermissionLevel.Manage, "Manage agent policies", "Create or edit agent execution policies."));

        // System agents — view-only
        _allList.Add(Permission("admin.system-agents.view", AdminArea.SystemAgents, AdminPermissionLevel.View, "View system agents", "View registered system agents."));

        // Workflow policies
        _allList.Add(Permission("admin.workflow-policies.view", AdminArea.WorkflowPolicies, AdminPermissionLevel.View, "View workflow policies", "View workflow execution policy configurations."));
        _allList.Add(Permission("admin.workflow-policies.manage", AdminArea.WorkflowPolicies, AdminPermissionLevel.Manage, "Manage workflow policies", "Create or edit workflow execution policies."));

        // MCP servers
        _allList.Add(Permission("admin.mcp-servers.view", AdminArea.McpServers, AdminPermissionLevel.View, "View MCP servers", "View registered Model Context Protocol servers."));
        _allList.Add(Permission("admin.mcp-servers.manage", AdminArea.McpServers, AdminPermissionLevel.Manage, "Manage MCP servers", "Add, edit, or remove MCP server registrations."));

        // Custom models (specs/072) — deliberately separate from ai-providers: deploying writes files to the production server.
        _allList.Add(Permission("admin.custom-models.view", AdminArea.CustomModels, AdminPermissionLevel.View, "View custom models", "View custom model deployments and their progress."));
        _allList.Add(Permission("admin.custom-models.manage", AdminArea.CustomModels, AdminPermissionLevel.Manage, "Manage custom models", "Deploy models from Hugging Face to the production server, cancel deployments, and change model availability."));

        All = new ReadOnlyCollection<AdminPermission>(_allList);
    }

    public static bool TryGet(string key, out AdminPermission? permission)
    {
        foreach (var item in _allList)
        {
            if (item.Key == key)
            {
                permission = item;
                return true;
            }
        }

        permission = null;
        return false;
    }

    public static IEnumerable<AdminPermission> ByArea(AdminArea area)
    {
        foreach (var p in _allList)
        {
            if (p.Area == area)
                yield return p;
        }
    }

    // --- helpers used in the constructor only ---

    private static AdminPermission Permission(
        string key,
        AdminArea area,
        AdminPermissionLevel level,
        string displayName,
        string description)
    {
        AdminPermission? implies = null;
        if (level == AdminPermissionLevel.Manage)
        {
            // Find the matching View permission in _allList (always already added when called in order).
            implies = _allList.FirstOrDefault(p => p.Area == area && p.Level == AdminPermissionLevel.View);
        }

        return new AdminPermission(key, area, level, displayName, description, implies);
    }
}
