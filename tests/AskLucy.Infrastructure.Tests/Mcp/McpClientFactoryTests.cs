using AskLucy.Application.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Mcp;
using AskLucy.Infrastructure.Mcp;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Mcp;

/// <summary>
/// spec.md FR-050, contracts/mcp-security-model.md — SSRF endpoint validation is re-run on every
/// new connection attempt, not cached from server registration (closes the DNS-rebinding gap
/// where a hostname resolved to a safe address at registration but now resolves to a private/
/// internal address). Verified without mocking the MCP SDK's connection layer: a rejection is
/// thrown before <c>McpClientFactory</c> ever reaches the SDK's <c>McpClient.CreateAsync</c> call.
/// </summary>
public sealed class McpClientFactoryTests
{
    private const string AdminId = "admin-1";

    private readonly IMcpServerRepository _serverRepository = Substitute.For<IMcpServerRepository>();
    private readonly IMcpEndpointValidator _endpointValidator = Substitute.For<IMcpEndpointValidator>();

    private McpClientFactory CreateFactory()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IMcpServerRepository)).Returns(_serverRepository);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        return new McpClientFactory(
            scopeFactory, Substitute.For<IMcpCredentialProtector>(), _endpointValidator,
            Microsoft.Extensions.Options.Options.Create(new McpRuntimeOptions()),
            Substitute.For<IHttpClientFactory>(), Substitute.For<ILoggerFactory>(), Substitute.For<ILogger<McpClientFactory>>());
    }

    private static McpServer RegisterServer() => McpServer.Register(
        "Test", null, "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey, false, false, null, false, null, AdminId, 60);

    [Fact]
    public async Task GetOrCreateAsync_ShouldRevalidateTheEndpoint_OnEveryNewConnectionAttempt_NotJustAtRegistration()
    {
        // Simulates DNS rebinding: the endpoint was Allowed when the server was registered
        // (elsewhere), but by the time a connection is actually attempted it now resolves
        // somewhere the validator rejects — proving the check runs fresh here, not a cached
        // registration-time result.
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetCredentialAsync(server.Id, Arg.Any<CancellationToken>()).Returns((McpServerCredential?)null);
        _endpointValidator.ValidateAsync(server.Endpoint, server.EndpointValidationOverride, Arg.Any<CancellationToken>())
            .Returns(McpEndpointValidationResult.RejectedPrivateOrLoopback);
        var factory = CreateFactory();

        var act = async () => await factory.GetOrCreateAsync(server.Id, CancellationToken.None);

        // Throws before ever reaching the SDK's actual connection attempt — proof the rejection
        // is enforced here, at connection time, independent of registration-time validation.
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*endpoint failed validation*");
        await _endpointValidator.Received(1).ValidateAsync(server.Endpoint, server.EndpointValidationOverride, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldThrow_WhenServerDoesNotExist()
    {
        _serverRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((McpServer?)null);
        var factory = CreateFactory();

        var act = async () => await factory.GetOrCreateAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
