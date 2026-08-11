using AskLucy.Application.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Commands.RotateMcpServerCredential;

/// <summary>spec.md FR-047 — replaces a server's credential material in place; never interrupts in-flight calls beyond the rotation itself (each call resolves its own connection independently via `IMcpClientFactory`).</summary>
public sealed record RotateMcpServerCredentialCommand(Guid ServerId, string NewCredential) : IRequest<McpServerDto>;
