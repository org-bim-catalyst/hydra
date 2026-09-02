using System.Collections.Concurrent;
using System.Diagnostics;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using SdkMcpClient = ModelContextProtocol.Client.McpClient;

namespace AskLucy.Infrastructure.Mcp;

/// <summary>
/// Resolves/creates a connected <see cref="IMcpClient"/> per <see cref="McpServer"/> (research.md
/// Decision 2). Registered as a <b>singleton</b> so a connection is reused across every execution
/// that calls the same server, not just within one DI scope — <see cref="McpToolAdapter"/>
/// instances live inside the singleton <c>IMcpToolRegistry</c>'s cache and must never hold a
/// reference to a Scoped dependency (constitution §3 — a captive dependency would throw
/// <see cref="ObjectDisposedException"/> the first time it's used from a different scope's job).
/// <see cref="IMcpServerRepository"/> is Scoped (EF Core-backed), so it is resolved from a
/// short-lived <see cref="IServiceScopeFactory"/> scope only for the duration of one
/// connect/version-check call, never held past that. Re-validates the endpoint via
/// <see cref="IMcpEndpointValidator"/> on every new connection, not only at registration
/// (FR-050, contracts/mcp-security-model.md).
/// </summary>
public sealed partial class McpClientFactory(
    IServiceScopeFactory scopeFactory,
    IMcpCredentialProtector credentialProtector,
    IMcpEndpointValidator endpointValidator,
    IOptions<McpRuntimeOptions> options,
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory,
    ILogger<McpClientFactory> logger) : IMcpClientFactory, IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, Task<(int ConfigurationVersion, IMcpClient Client)>> _connections = new();

    public async Task<IMcpClient> GetOrCreateAsync(Guid mcpServerId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var serverRepository = scope.ServiceProvider.GetRequiredService<IMcpServerRepository>();

        var server = await serverRepository.GetByIdAsync(mcpServerId, cancellationToken)
            ?? throw new InvalidOperationException($"MCP server {mcpServerId} was not found.");

        if (_connections.TryGetValue(mcpServerId, out var existing))
        {
            var (configurationVersion, client) = await existing;
            if (configurationVersion == server.ConfigurationVersion)
            {
                return client;
            }

            // Configuration changed since the connection was established — discard and reconnect.
            await client.DisposeAsync();
            _connections.TryRemove(mcpServerId, out _);
        }

        var credential = await serverRepository.GetCredentialAsync(server.Id, cancellationToken);
        var created = _connections.GetOrAdd(mcpServerId, _ => ConnectAsync(server, credential, cancellationToken));
        var (_, newClient) = await created;
        return newClient;
    }

    public async Task InvalidateConnectionAsync(Guid mcpServerId, CancellationToken cancellationToken = default)
    {
        if (_connections.TryRemove(mcpServerId, out var existing))
        {
            var (_, client) = await existing;
            await client.DisposeAsync();
        }
    }

    private async Task<(int ConfigurationVersion, IMcpClient Client)> ConnectAsync(McpServer server, McpServerCredential? credential, CancellationToken cancellationToken)
    {
        var validation = await endpointValidator.ValidateAsync(server.Endpoint, server.EndpointValidationOverride, cancellationToken);
        if (validation != McpEndpointValidationResult.Allowed)
        {
            throw new InvalidOperationException($"MCP server {server.Id}'s endpoint failed validation ({validation}) at connection time.");
        }

        var transport = BuildTransport(server, credential);

        var stopwatch = Stopwatch.StartNew();
        var sdkClient = await SdkMcpClient.CreateAsync(transport, loggerFactory: loggerFactory, cancellationToken: cancellationToken);
        // FR-057 — connection latency, discoverable through the platform's existing
        // observability capability (structured Serilog logging, constitution §14).
        LogConnectionEstablished(server.Id, stopwatch.ElapsedMilliseconds);
        return (server.ConfigurationVersion, new McpClient(sdkClient));
    }

    private IClientTransport BuildTransport(McpServer server, McpServerCredential? credential)
    {
        if (server.Transport == McpServerTransport.Stdio)
        {
            if (!options.Value.AllowLocalTransport)
            {
                throw new InvalidOperationException("Local (stdio) MCP transport is not enabled for this deployment (FR-009).");
            }

            // FR-010 — the command is exactly the administrator-registered, pre-approved
            // configuration; there is no code path from user input to this value.
            return new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = server.Endpoint,
                Name = server.Name,
            }, loggerFactory);
        }

        var httpOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(server.Endpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = TimeSpan.FromSeconds(options.Value.MaxCallDurationSeconds),
            Name = server.Name,
        };

        if (credential is not null && server.AuthenticationType != McpAuthenticationType.None)
        {
            var rawCredential = credentialProtector.Unprotect(credential.CiphertextBlob);
            var headerValue = server.AuthenticationType switch
            {
                McpAuthenticationType.ApiKey => rawCredential,
                // OAuth2ClientCredentials is applied as a pre-obtained bearer token (rotated via
                // the existing credential-rotation mechanism) rather than Ask Lucy performing the
                // RFC 6749 client-credentials token exchange itself — a deliberate v1
                // simplification (Open Questions, spec.md).
                McpAuthenticationType.BearerToken or McpAuthenticationType.OAuth2ClientCredentials => $"Bearer {rawCredential}",
                _ => rawCredential,
            };
            httpOptions.AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = headerValue };
        }

        return new HttpClientTransport(httpOptions, httpClientFactory.CreateClient("Mcp"), loggerFactory, ownsHttpClient: false);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections.Values)
        {
            if (connection.IsCompletedSuccessfully)
            {
                await connection.Result.Client.DisposeAsync();
            }
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "MCP connection established to server {McpServerId} in {ElapsedMilliseconds}ms")]
    private partial void LogConnectionEstablished(Guid mcpServerId, long elapsedMilliseconds);
}
