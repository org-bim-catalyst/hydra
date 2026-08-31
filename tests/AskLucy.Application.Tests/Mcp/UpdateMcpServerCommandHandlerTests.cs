using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Commands.UpdateMcpServer;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class UpdateMcpServerCommandHandlerTests
{
    private const string AdminId = "admin-1";

    private readonly IMcpServerRepository _serverRepository = Substitute.For<IMcpServerRepository>();
    private readonly IMcpAuditLogRepository _auditLogRepository = Substitute.For<IMcpAuditLogRepository>();
    private readonly IMcpEndpointValidator _endpointValidator = Substitute.For<IMcpEndpointValidator>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private UpdateMcpServerCommandHandler CreateHandler() => new(_serverRepository, _auditLogRepository, _endpointValidator, _unitOfWork, _currentUser);

    private static McpServer RegisterServer() => McpServer.Register(
        "Original", "desc", "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey,
        false, false, null, false, null, AdminId, 60);

    public UpdateMcpServerCommandHandlerTests()
    {
        _currentUser.UserId.Returns(AdminId);
        _endpointValidator.ValidateAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(McpEndpointValidationResult.Allowed);
    }

    private static UpdateMcpServerCommand ValidCommand(Guid id, string endpoint = "https://mcp.example.com", bool allowInsecureTransport = false, string? insecureJustification = null, bool endpointOverride = false, string? endpointJustification = null) => new(
        id, "Renamed", "new desc", endpoint, McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey,
        false, allowInsecureTransport, insecureJustification, endpointOverride, endpointJustification, 30);

    [Fact]
    public async Task Handle_ShouldThrowMcpEndpointNotAllowedException_WhenTheNewEndpointFailsSsrfValidation()
    {
        // The endpoint that passed validation at registration is being changed to one the
        // validator now rejects — proves re-validation runs on every update, not only once at
        // registration.
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _endpointValidator.ValidateAsync("https://10.0.0.5/", Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(McpEndpointValidationResult.RejectedPrivateOrLoopback);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(ValidCommand(server.Id, endpoint: "https://10.0.0.5/"), CancellationToken.None);

        await act.Should().ThrowAsync<McpEndpointNotAllowedException>();
    }

    [Fact]
    public async Task Handle_ShouldIncrementConfigurationVersion()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        var handler = CreateHandler();

        var result = await handler.Handle(ValidCommand(server.Id), CancellationToken.None);

        result.ConfigurationVersion.Should().Be(2);
        result.Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenServerNotFound()
    {
        _serverRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((McpServer?)null);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(ValidCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenInsecureTransportAllowedWithoutJustification()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(ValidCommand(server.Id, allowInsecureTransport: true, insecureJustification: null), CancellationToken.None);

        await act.Should().ThrowAsync<AskLucy.Domain.Common.DomainRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ShouldSucceed_WhenInsecureTransportAllowedWithJustification()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        var handler = CreateHandler();

        var result = await handler.Handle(ValidCommand(server.Id, allowInsecureTransport: true, insecureJustification: "dev environment"), CancellationToken.None);

        result.AllowInsecureTransport.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldThrowDuplicateResourceException_WhenChangingToAnotherServersEndpoint()
    {
        var server = RegisterServer();
        var otherServer = McpServer.Register("Other", null, "https://other.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey, false, false, null, false, null, AdminId, 60);
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetByEndpointAndTransportAsync("https://other.example.com", McpServerTransport.StreamableHttp, Arg.Any<CancellationToken>()).Returns(otherServer);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(ValidCommand(server.Id, endpoint: "https://other.example.com"), CancellationToken.None);

        await act.Should().ThrowAsync<AskLucy.Domain.Common.DuplicateResourceException>();
    }

    [Fact]
    public async Task Handle_ShouldRecordAuditLog()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        var handler = CreateHandler();

        await handler.Handle(ValidCommand(server.Id), CancellationToken.None);

        _auditLogRepository.Received(1).Add(Arg.Is<McpAuditLog>(a => a!.Action == McpAuditAction.ServerUpdated));
    }
}
