using AskLucy.Application.Abstractions;
using AskLucy.Domain.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Commands.UpdateMcpServer;

public sealed class UpdateMcpServerCommandHandler(
    IMcpServerRepository serverRepository,
    IMcpAuditLogRepository auditLogRepository,
    IMcpEndpointValidator endpointValidator,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<UpdateMcpServerCommand, McpServerDto>
{
    public async Task<McpServerDto> Handle(UpdateMcpServerCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var server = await serverRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"MCP server {request.Id} was not found.");

        if (request.Transport == McpServerTransport.StreamableHttp)
        {
            var validation = await endpointValidator.ValidateAsync(request.Endpoint, request.EndpointValidationOverride, cancellationToken);
            if (validation != McpEndpointValidationResult.Allowed)
            {
                throw new McpEndpointNotAllowedException(request.Endpoint, validation.ToString());
            }
        }

        if (request.Endpoint != server.Endpoint || request.Transport != server.Transport)
        {
            var conflicting = await serverRepository.GetByEndpointAndTransportAsync(request.Endpoint, request.Transport, cancellationToken);
            if (conflicting is not null && conflicting.Id != server.Id)
            {
                throw new AskLucy.Domain.Common.DuplicateResourceException(
                    $"An MCP server with endpoint '{request.Endpoint}' and transport '{request.Transport}' is already registered (id: {conflicting.Id}).");
            }
        }

        server.UpdateConfiguration(
            request.Name, request.Description, request.Endpoint, request.Transport, request.AuthenticationType,
            request.RequiresUnauthenticatedConfirmation, request.AllowInsecureTransport, request.InsecureTransportJustification,
            request.EndpointValidationOverride, request.EndpointValidationJustification, request.CapabilityRefreshIntervalMinutes, userId);

        auditLogRepository.Add(McpAuditLog.Record(server.Id, userId, McpAuditAction.ServerUpdated, null, $$"""{"configurationVersion":{{server.ConfigurationVersion}}}"""));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return McpServerDto.Create(server);
    }
}
