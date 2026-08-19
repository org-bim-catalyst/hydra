using AskLucy.Application.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.ListMcpServerReferences;

/// <summary>spec.md FR-065 — same join <c>DeleteMcpServerCommand</c>'s 422 body surfaces, exposed proactively so an admin can check before attempting removal.</summary>
public sealed record ListMcpServerReferencesQuery(Guid Id) : IRequest<IReadOnlyList<McpServerReferenceDto>>;
