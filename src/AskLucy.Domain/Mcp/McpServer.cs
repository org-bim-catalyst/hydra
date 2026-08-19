using AskLucy.Domain.Common;

namespace AskLucy.Domain.Mcp;

public enum McpServerTransport
{
    StreamableHttp,
    Stdio,
}

public enum McpAuthenticationType
{
    None,
    ApiKey,
    BearerToken,
    OAuth2ClientCredentials,
}

/// <summary>
/// The administrator-registered external MCP server (spec.md FR-001-FR-010, data-model.md).
/// Starts <see cref="IsEnabled"/> false on registration — an administrator must explicitly
/// enable it after a successful test connection and capability discovery (contracts/mcp-lifecycle-events.md).
/// </summary>
public sealed class McpServer : BaseEntity
{
    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string Endpoint { get; private set; } = string.Empty;

    public McpServerTransport Transport { get; private set; }

    public McpAuthenticationType AuthenticationType { get; private set; }

    public bool RequiresUnauthenticatedConfirmation { get; private set; }

    public bool AllowInsecureTransport { get; private set; }

    public string? InsecureTransportJustification { get; private set; }

    public bool EndpointValidationOverride { get; private set; }

    public string? EndpointValidationJustification { get; private set; }

    public bool IsEnabled { get; private set; }

    public string OwnerUserId { get; private set; } = string.Empty;

    public int ConfigurationVersion { get; private set; }

    public int CapabilityRefreshIntervalMinutes { get; private set; }

    public DateTime? LastHealthCheckAtUtc { get; private set; }

    public DateTime? LastCapabilityDiscoveryAtUtc { get; private set; }

    private McpServer()
    {
        // Required by EF Core materialization.
    }

    public static McpServer Register(
        string name,
        string? description,
        string endpoint,
        McpServerTransport transport,
        McpAuthenticationType authenticationType,
        bool requiresUnauthenticatedConfirmation,
        bool allowInsecureTransport,
        string? insecureTransportJustification,
        bool endpointValidationOverride,
        string? endpointValidationJustification,
        string ownerUserId,
        int capabilityRefreshIntervalMinutes)
    {
        ValidateFields(
            name, endpoint, transport, authenticationType, requiresUnauthenticatedConfirmation,
            allowInsecureTransport, insecureTransportJustification, endpointValidationOverride, endpointValidationJustification);

        return new McpServer
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Description = description,
            Endpoint = endpoint.Trim(),
            Transport = transport,
            AuthenticationType = authenticationType,
            RequiresUnauthenticatedConfirmation = requiresUnauthenticatedConfirmation,
            AllowInsecureTransport = allowInsecureTransport,
            InsecureTransportJustification = insecureTransportJustification,
            EndpointValidationOverride = endpointValidationOverride,
            EndpointValidationJustification = endpointValidationJustification,
            IsEnabled = false,
            OwnerUserId = ownerUserId,
            ConfigurationVersion = 1,
            CapabilityRefreshIntervalMinutes = capabilityRefreshIntervalMinutes,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = ownerUserId,
        };
    }

    public void UpdateConfiguration(
        string name,
        string? description,
        string endpoint,
        McpServerTransport transport,
        McpAuthenticationType authenticationType,
        bool requiresUnauthenticatedConfirmation,
        bool allowInsecureTransport,
        string? insecureTransportJustification,
        bool endpointValidationOverride,
        string? endpointValidationJustification,
        int capabilityRefreshIntervalMinutes,
        string actor)
    {
        ValidateFields(
            name, endpoint, transport, authenticationType, requiresUnauthenticatedConfirmation,
            allowInsecureTransport, insecureTransportJustification, endpointValidationOverride, endpointValidationJustification);

        Name = name.Trim();
        Description = description;
        Endpoint = endpoint.Trim();
        Transport = transport;
        AuthenticationType = authenticationType;
        RequiresUnauthenticatedConfirmation = requiresUnauthenticatedConfirmation;
        AllowInsecureTransport = allowInsecureTransport;
        InsecureTransportJustification = insecureTransportJustification;
        EndpointValidationOverride = endpointValidationOverride;
        EndpointValidationJustification = endpointValidationJustification;
        CapabilityRefreshIntervalMinutes = capabilityRefreshIntervalMinutes;
        ConfigurationVersion++;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Enable(string actor)
    {
        IsEnabled = true;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Disable(string actor)
    {
        IsEnabled = false;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void RecordHealthCheck(DateTime occurredAtUtc)
    {
        LastHealthCheckAtUtc = occurredAtUtc;
    }

    public void RecordCapabilityDiscovery(DateTime occurredAtUtc)
    {
        LastCapabilityDiscoveryAtUtc = occurredAtUtc;
    }

    public void SoftDelete(string actor)
    {
        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = actor;
    }

    private static void ValidateFields(
        string name,
        string endpoint,
        McpServerTransport transport,
        McpAuthenticationType authenticationType,
        bool requiresUnauthenticatedConfirmation,
        bool allowInsecureTransport,
        string? insecureTransportJustification,
        bool endpointValidationOverride,
        string? endpointValidationJustification)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A server name is required.");
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new DomainRuleViolationException("A server endpoint is required.");
        }

        if (endpoint.Length > 400)
        {
            throw new DomainRuleViolationException("A server endpoint must be 400 characters or fewer.");
        }

        if (transport == McpServerTransport.StreamableHttp
            && authenticationType == McpAuthenticationType.None
            && !requiresUnauthenticatedConfirmation)
        {
            throw new DomainRuleViolationException(
                "A remote server with no authentication requires explicit administrator confirmation (FR-048).");
        }

        if (allowInsecureTransport && string.IsNullOrWhiteSpace(insecureTransportJustification))
        {
            throw new DomainRuleViolationException(
                "Allowing an insecure transport requires a documented justification (FR-049).");
        }

        if (endpointValidationOverride && string.IsNullOrWhiteSpace(endpointValidationJustification))
        {
            throw new DomainRuleViolationException(
                "Overriding endpoint validation requires a documented justification (FR-050).");
        }
    }
}
