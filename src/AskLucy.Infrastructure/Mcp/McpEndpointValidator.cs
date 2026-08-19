using System.Net;
using System.Net.Sockets;
using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Mcp;

/// <summary>
/// SSRF protection for MCP server endpoints (spec.md FR-050, research.md Decision 8,
/// contracts/mcp-security-model.md) — the first utility of this kind in this codebase; no prior
/// private-IP/DNS-guard utility exists to reuse.
/// </summary>
public sealed class McpEndpointValidator(ILogger<McpEndpointValidator> logger) : IMcpEndpointValidator
{
    public async Task<McpEndpointValidationResult> ValidateAsync(string endpoint, bool allowOverride, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return McpEndpointValidationResult.RejectedUnresolvable;
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            return McpEndpointValidationResult.RejectedInsecureScheme;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
        }
        catch (SocketException ex)
        {
            logger.LogWarning(ex, "DNS resolution failed for MCP endpoint host {Host}", uri.Host);
            return McpEndpointValidationResult.RejectedUnresolvable;
        }

        if (addresses.Length == 0)
        {
            return McpEndpointValidationResult.RejectedUnresolvable;
        }

        if (allowOverride)
        {
            return McpEndpointValidationResult.Allowed;
        }

        foreach (var address in addresses)
        {
            if (IsLoopbackOrPrivate(address))
            {
                return McpEndpointValidationResult.RejectedPrivateOrLoopback;
            }

            if (IsLinkLocalOrCloudMetadata(address))
            {
                return McpEndpointValidationResult.RejectedLinkLocalOrCloudMetadata;
            }
        }

        return McpEndpointValidationResult.Allowed;
    }

    private static bool IsLoopbackOrPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var octets = address.GetAddressBytes();

            // 10.0.0.0/8
            if (octets[0] == 10)
            {
                return true;
            }

            // 172.16.0.0/12
            if (octets[0] == 172 && octets[1] is >= 16 and <= 31)
            {
                return true;
            }

            // 192.168.0.0/16
            if (octets[0] == 192 && octets[1] == 168)
            {
                return true;
            }
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // fc00::/7 (unique local addresses)
            var firstByte = address.GetAddressBytes()[0];
            if ((firstByte & 0xFE) == 0xFC)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLinkLocalOrCloudMetadata(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var octets = address.GetAddressBytes();

            // 169.254.0.0/16 — includes the 169.254.169.254 cloud-metadata endpoint shared by
            // AWS/Azure/GCP instance metadata services.
            return octets[0] == 169 && octets[1] == 254;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal;
        }

        return false;
    }
}
