using AskLucy.Domain.Mcp;

namespace AskLucy.Application.Abstractions;

public interface IMcpPromptRepository
{
    Task<McpPrompt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<McpPrompt?> GetByNamespacedNameAsync(string namespacedName, CancellationToken cancellationToken = default);

    void Add(McpPrompt prompt);

    Task<IReadOnlyList<McpPrompt>> ListByServerIdAsync(Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>FR-042 catalog listing — includes each prompt's source server name (mirrors <c>IMcpToolRepository.ListActiveAvailableAsync</c>'s shape). A disabled/removed source server excludes the prompt (FR-044), the same join <c>ListActiveAvailableAsync</c> uses for tools.</summary>
    Task<IReadOnlyList<(McpPrompt Prompt, string ServerName)>> ListAvailableAsync(CancellationToken cancellationToken = default);
}
