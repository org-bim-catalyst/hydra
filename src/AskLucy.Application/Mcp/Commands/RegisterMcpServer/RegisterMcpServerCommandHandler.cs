using AskLucy.Application.Abstractions;
using AskLucy.Domain.Common;
using AskLucy.Domain.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Commands.RegisterMcpServer;

public sealed class RegisterMcpServerCommandHandler(
    IMcpServerRepository serverRepository,
    IMcpAuditLogRepository auditLogRepository,
    IMcpEndpointValidator endpointValidator,
    IMcpCredentialProtector credentialProtector,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<RegisterMcpServerCommand, McpServerDto>
{
    public async Task<McpServerDto> Handle(RegisterMcpServerCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        // FR-050 — SSRF validation before insert, independent of the domain's own field validation.
        if (request.Transport == McpServerTransport.StreamableHttp)
        {
            var validation = await endpointValidator.ValidateAsync(request.Endpoint, request.EndpointValidationOverride, cancellationToken);
            if (validation != McpEndpointValidationResult.Allowed)
            {
                throw new McpEndpointNotAllowedException(request.Endpoint, validation.ToString());
            }
        }

        // Clarification — (Endpoint, Transport) is unique platform-wide.
        var existing = await serverRepository.GetByEndpointAndTransportAsync(request.Endpoint, request.Transport, cancellationToken);
        if (existing is not null)
        {
            throw new DuplicateResourceException($"An MCP server with endpoint '{request.Endpoint}' and transport '{request.Transport}' is already registered (id: {existing.Id}).");
        }

        var server = McpServer.Register(
            request.Name, request.Description, request.Endpoint, request.Transport, request.AuthenticationType,
            request.RequiresUnauthenticatedConfirmation, request.AllowInsecureTransport, request.InsecureTransportJustification,
            request.EndpointValidationOverride, request.EndpointValidationJustification, userId, request.CapabilityRefreshIntervalMinutes);
        serverRepository.Add(server);

        if (!string.IsNullOrWhiteSpace(request.Credential))
        {
            var ciphertext = credentialProtector.Protect(request.Credential);
            var credential = McpServerCredential.Create(server.Id, ciphertext, userId);
            serverRepository.AddCredential(credential);
        }

        auditLogRepository.Add(McpAuditLog.Record(server.Id, userId, McpAuditAction.ServerRegistered, null, $$"""{"name":"{{server.Name}}"}"""));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return McpServerDto.Create(server);
    }
}
