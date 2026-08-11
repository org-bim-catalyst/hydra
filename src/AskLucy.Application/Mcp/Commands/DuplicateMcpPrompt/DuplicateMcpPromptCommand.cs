using AskLucy.Application.Prompts;
using MediatR;

namespace AskLucy.Application.Mcp.Commands.DuplicateMcpPrompt;

/// <summary>spec.md FR-041-FR-044, research.md Decision 16 — creates a new, independent, user-owned native `Prompt` seeded from an `McpPrompt`'s current `ContentTemplate`; the source `McpPrompt` is a read-only mirror with no relationship to the copy afterward, mirroring spec 019's `DuplicatePromptCommand`.</summary>
public sealed record DuplicateMcpPromptCommand(string NamespacedName) : IRequest<PromptDetailDto>;
