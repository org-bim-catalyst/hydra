using AskLucy.Domain.Common;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Mcp;

public sealed class McpServerTests
{
    private const string Actor = "admin-1";

    private static McpServer RegisterServer(
        McpServerTransport transport = McpServerTransport.StreamableHttp,
        McpAuthenticationType authenticationType = McpAuthenticationType.ApiKey,
        bool requiresUnauthenticatedConfirmation = false,
        bool allowInsecureTransport = false,
        string? insecureTransportJustification = null,
        bool endpointValidationOverride = false,
        string? endpointValidationJustification = null) =>
        McpServer.Register(
            "Test Server", "desc", "https://mcp.example.com", transport, authenticationType,
            requiresUnauthenticatedConfirmation, allowInsecureTransport, insecureTransportJustification,
            endpointValidationOverride, endpointValidationJustification, Actor, capabilityRefreshIntervalMinutes: 60);

    [Fact]
    public void Register_ShouldStartDisabled_WithConfigurationVersionOne()
    {
        var server = RegisterServer();

        server.IsEnabled.Should().BeFalse();
        server.ConfigurationVersion.Should().Be(1);
    }

    [Fact]
    public void Register_ShouldThrow_WhenRemoteServerHasNoAuthenticationAndNoConfirmation()
    {
        var act = () => RegisterServer(authenticationType: McpAuthenticationType.None, requiresUnauthenticatedConfirmation: false);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Register_ShouldSucceed_WhenUnauthenticatedIsExplicitlyConfirmed()
    {
        var server = RegisterServer(authenticationType: McpAuthenticationType.None, requiresUnauthenticatedConfirmation: true);

        server.AuthenticationType.Should().Be(McpAuthenticationType.None);
    }

    [Fact]
    public void Register_ShouldThrow_WhenInsecureTransportAllowedWithoutJustification()
    {
        var act = () => RegisterServer(allowInsecureTransport: true, insecureTransportJustification: null);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Register_ShouldThrow_WhenEndpointValidationOverriddenWithoutJustification()
    {
        var act = () => RegisterServer(endpointValidationOverride: true, endpointValidationJustification: null);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Enable_ThenDisable_ShouldToggleIsEnabled()
    {
        var server = RegisterServer();

        server.Enable(Actor);
        server.IsEnabled.Should().BeTrue();

        server.Disable(Actor);
        server.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void UpdateConfiguration_ShouldIncrementConfigurationVersion()
    {
        var server = RegisterServer();

        server.UpdateConfiguration(
            "Renamed", "desc", "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey,
            requiresUnauthenticatedConfirmation: false, allowInsecureTransport: false, insecureTransportJustification: null,
            endpointValidationOverride: false, endpointValidationJustification: null, capabilityRefreshIntervalMinutes: 30, Actor);

        server.ConfigurationVersion.Should().Be(2);
        server.Name.Should().Be("Renamed");
    }
}
