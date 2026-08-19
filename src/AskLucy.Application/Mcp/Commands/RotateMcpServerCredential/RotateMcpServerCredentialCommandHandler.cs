using AskLucy.Application.Abstractions;
using AskLucy.Domain.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Commands.RotateMcpServerCredential;

public sealed class RotateMcpServerCredentialCommandHandler(
    IMcpServerRepository serverRepository,
    IMcpAuditLogRepository auditLogRepository,
    IMcpCredentialProtector credentialProtector,
    IMcpClientFactory clientFactory,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<RotateMcpServerCredentialCommand, McpServerDto>
{
    public async Task<McpServerDto> Handle(RotateMcpServerCredentialCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var server = await serverRepository.GetByIdAsync(request.ServerId, cancellationToken)
            ?? throw new KeyNotFoundException($"MCP server {request.ServerId} was not found.");

        var ciphertext = credentialProtector.Protect(request.NewCredential);
        var existingCredential = await serverRepository.GetCredentialAsync(server.Id, cancellationToken);
        if (existingCredential is not null)
        {
            // FR-047 — in-place replacement, never delete+re-insert (preserves the row's identity
            // and creation history).
            existingCredential.Rotate(ciphertext, userId);
        }
        else
        {
            serverRepository.AddCredential(McpServerCredential.Create(server.Id, ciphertext, userId));
        }

        // FR-046/FR-059 — never the credential value itself, in any form.
        auditLogRepository.Add(McpAuditLog.Record(server.Id, userId, McpAuditAction.CredentialRotated, null, "{}"));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // FR-047 — the next call reconnects with the new credential; an already in-flight call on
        // the old connection is unaffected and completes/fails on its own terms.
        await clientFactory.InvalidateConnectionAsync(server.Id, cancellationToken);

        return McpServerDto.Create(server);
    }
}
