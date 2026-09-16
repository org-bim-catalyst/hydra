using AskLucy.Application.Common;
using AskLucy.Application.Mcp;
using AskLucy.Application.Mcp.Commands.ActivateMcpTool;
using AskLucy.Application.Mcp.Commands.DeactivateMcpTool;
using AskLucy.Application.Mcp.Commands.DeleteMcpServer;
using AskLucy.Application.Mcp.Commands.DisableMcpServer;
using AskLucy.Application.Mcp.Commands.EnableMcpServer;
using AskLucy.Application.Mcp.Commands.RefreshMcpCapabilities;
using AskLucy.Application.Mcp.Commands.RegisterMcpServer;
using AskLucy.Application.Mcp.Commands.RotateMcpServerCredential;
using AskLucy.Application.Mcp.Commands.TestMcpServerConnection;
using AskLucy.Application.Mcp.Commands.UpdateMcpServer;
using AskLucy.Application.Mcp.Queries.GetMcpServer;
using AskLucy.Application.Mcp.Queries.GetMcpServerHealth;
using AskLucy.Application.Mcp.Queries.ListMcpAuditLog;
using AskLucy.Application.Mcp.Queries.ListMcpServerReferences;
using AskLucy.Application.Mcp.Queries.ListMcpServers;
using AskLucy.Application.Mcp.Queries.ListMcpServerTools;
using AskLucy.Domain.Mcp;
using AskLucy.Web.Auth;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>MCP server registry administration (contracts/mcp-api.md) — permission-gated (specs/055-role-management research.md Decision 5).</summary>
[ApiController]
[EnableRateLimiting("mcp-admin-endpoints")]
[Route("api/v1/admin/mcp/servers")]
public sealed class McpServersController(ISender mediator) : ControllerBase
{
    [HttpPost]
    [RequirePermission("admin.mcp-servers.manage")]
    public async Task<ActionResult<McpServerDto>> Register([FromBody] RegisterMcpServerRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new RegisterMcpServerCommand(
                request.Name, request.Description, request.Endpoint, request.Transport, request.AuthenticationType,
                request.Credential, request.RequiresUnauthenticatedConfirmation, request.AllowInsecureTransport,
                request.InsecureTransportJustification, request.EndpointValidationOverride, request.EndpointValidationJustification,
                request.CapabilityRefreshIntervalMinutes),
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpGet]
    [RequirePermission("admin.mcp-servers.view")]
    public async Task<ActionResult<PagedResult<McpServerDto>>> List(
        [FromQuery] McpServerHealthStatus? status = null,
        [FromQuery] McpServerTransport? transport = null,
        [FromQuery] bool? enabled = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new ListMcpServersQuery(status, transport, enabled, cursor, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    [RequirePermission("admin.mcp-servers.view")]
    public async Task<ActionResult<McpServerDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetMcpServerQuery(id), cancellationToken));

    [HttpPut("{id:guid}")]
    [RequirePermission("admin.mcp-servers.manage")]
    public async Task<ActionResult<McpServerDto>> Update(Guid id, [FromBody] UpdateMcpServerRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(
            new UpdateMcpServerCommand(
                id, request.Name, request.Description, request.Endpoint, request.Transport, request.AuthenticationType,
                request.RequiresUnauthenticatedConfirmation, request.AllowInsecureTransport, request.InsecureTransportJustification,
                request.EndpointValidationOverride, request.EndpointValidationJustification, request.CapabilityRefreshIntervalMinutes),
            cancellationToken));

    [HttpDelete("{id:guid}")]
    [RequirePermission("admin.mcp-servers.manage")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteMcpServerCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/actions/enable")]
    [RequirePermission("admin.mcp-servers.manage")]
    public async Task<ActionResult<McpServerDto>> Enable(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new EnableMcpServerCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/disable")]
    [RequirePermission("admin.mcp-servers.manage")]
    public async Task<ActionResult<McpServerDto>> Disable(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new DisableMcpServerCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/test-connection")]
    [RequirePermission("admin.mcp-servers.manage")]
    public async Task<ActionResult<McpServerHealthDto>> TestConnection(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new TestMcpServerConnectionCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/refresh-capabilities")]
    [RequirePermission("admin.mcp-servers.manage")]
    public async Task<ActionResult<McpCapabilityRefreshResultDto>> RefreshCapabilities(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new RefreshMcpCapabilitiesCommand(id), cancellationToken));

    [HttpPost("{id:guid}/actions/rotate-credential")]
    [RequirePermission("admin.mcp-servers.manage")]
    public async Task<ActionResult<McpServerDto>> RotateCredential(Guid id, [FromBody] RotateMcpServerCredentialRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new RotateMcpServerCredentialCommand(id, request.Credential), cancellationToken));

    [HttpGet("{id:guid}/health")]
    [RequirePermission("admin.mcp-servers.view")]
    public async Task<ActionResult<McpServerHealthDto>> GetHealth(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetMcpServerHealthQuery(id), cancellationToken));

    [HttpGet("{id:guid}/references")]
    [RequirePermission("admin.mcp-servers.view")]
    public async Task<ActionResult<IReadOnlyList<McpServerReferenceDto>>> ListReferences(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListMcpServerReferencesQuery(id), cancellationToken));

    [HttpGet("{id:guid}/tools")]
    [RequirePermission("admin.mcp-servers.view")]
    public async Task<ActionResult<IReadOnlyList<McpToolDto>>> ListTools(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListMcpServerToolsQuery(id), cancellationToken));

    [HttpGet("{id:guid}/audit-log")]
    [RequirePermission("admin.mcp-servers.view")]
    public async Task<ActionResult<PagedResult<McpAuditLogDto>>> ListAuditLog(
        Guid id, [FromQuery] string? cursor = null, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new ListMcpAuditLogQuery(id, cursor, pageSize), cancellationToken));

    [HttpPost("{id:guid}/tools/{toolId:guid}/actions/activate")]
    [RequirePermission("admin.mcp-servers.manage")]
    public async Task<ActionResult<McpToolDto>> ActivateTool(
        Guid id, Guid toolId, [FromBody] ActivateMcpToolRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ActivateMcpToolCommand(id, toolId, request.EffectiveRiskLevelOverride, request.RequiredPermissionsJsonOverride), cancellationToken));

    [HttpPost("{id:guid}/tools/{toolId:guid}/actions/deactivate")]
    [RequirePermission("admin.mcp-servers.manage")]
    public async Task<ActionResult<McpToolDto>> DeactivateTool(Guid id, Guid toolId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new DeactivateMcpToolCommand(id, toolId), cancellationToken));
}
