using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Commands.RegisterMcpServer;
using AskLucy.Domain.Common;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class RegisterMcpServerCommandHandlerTests
{
    private const string AdminId = "admin-1";

    private readonly IMcpServerRepository _serverRepository = Substitute.For<IMcpServerRepository>();
    private readonly IMcpAuditLogRepository _auditLogRepository = Substitute.For<IMcpAuditLogRepository>();
    private readonly IMcpEndpointValidator _endpointValidator = Substitute.For<IMcpEndpointValidator>();
    private readonly IMcpCredentialProtector _credentialProtector = Substitute.For<IMcpCredentialProtector>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private RegisterMcpServerCommandHandler CreateHandler() => new(
        _serverRepository, _auditLogRepository, _endpointValidator, _credentialProtector, _unitOfWork, _currentUser);

    private static RegisterMcpServerCommand ValidCommand(
        McpAuthenticationType authenticationType = McpAuthenticationType.ApiKey,
        bool requiresUnauthenticatedConfirmation = false,
        bool allowInsecureTransport = false,
        string? insecureTransportJustification = null,
        bool endpointValidationOverride = false,
        string? endpointValidationJustification = null) => new(
        "Test Server", "desc", "https://mcp.example.com", McpServerTransport.StreamableHttp, authenticationType,
        "raw-api-key", requiresUnauthenticatedConfirmation, allowInsecureTransport, insecureTransportJustification,
        endpointValidationOverride, endpointValidationJustification, 60);

    public RegisterMcpServerCommandHandlerTests()
    {
        _currentUser.UserId.Returns(AdminId);
        _endpointValidator.ValidateAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(McpEndpointValidationResult.Allowed);
        _credentialProtector.Protect(Arg.Any<string>()).Returns(ci => $"encrypted:{ci.Arg<string>()}");
    }

    [Fact]
    public async Task Handle_ShouldRegisterServer_WhenEndpointAllowedAndNotDuplicate()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.Name.Should().Be("Test Server");
        result.IsEnabled.Should().BeFalse();
        _serverRepository.Received(1).Add(Arg.Any<McpServer>());
        _serverRepository.Received(1).AddCredential(Arg.Any<McpServerCredential>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldEncryptCredential_BeforePersisting()
    {
        var handler = CreateHandler();

        await handler.Handle(ValidCommand(), CancellationToken.None);

        _credentialProtector.Received(1).Protect("raw-api-key");
        _serverRepository.Received(1).AddCredential(Arg.Is<McpServerCredential>(c => c.CiphertextBlob == "encrypted:raw-api-key"));
    }

    [Fact]
    public async Task Handle_ShouldThrowMcpEndpointNotAllowedException_WhenSsrfValidationRejects()
    {
        _endpointValidator.ValidateAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(McpEndpointValidationResult.RejectedPrivateOrLoopback);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<McpEndpointNotAllowedException>();
        _serverRepository.DidNotReceive().Add(Arg.Any<McpServer>());
    }

    [Fact]
    public async Task Handle_ShouldThrowDuplicateResourceException_WhenEndpointAndTransportAlreadyRegistered()
    {
        _serverRepository.GetByEndpointAndTransportAsync(Arg.Any<string>(), Arg.Any<McpServerTransport>(), Arg.Any<CancellationToken>())
            .Returns(McpServer.Register("Existing", null, "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey, false, false, null, false, null, AdminId, 60));
        var handler = CreateHandler();

        var act = async () => await handler.Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateResourceException>();
    }

    [Fact]
    public async Task Handle_ShouldThrowDomainRuleViolationException_WhenUnauthenticatedRemoteServerNotConfirmed()
    {
        var handler = CreateHandler();
        var command = ValidCommand(authenticationType: McpAuthenticationType.None, requiresUnauthenticatedConfirmation: false);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ShouldSucceed_WhenUnauthenticatedIsExplicitlyConfirmed()
    {
        var handler = CreateHandler();
        var command = ValidCommand(authenticationType: McpAuthenticationType.None, requiresUnauthenticatedConfirmation: true) with { Credential = null };

        var result = await handler.Handle(command, CancellationToken.None);

        result.AuthenticationType.Should().Be(McpAuthenticationType.None);
        _serverRepository.DidNotReceive().AddCredential(Arg.Any<McpServerCredential>());
    }

    [Fact]
    public async Task Handle_ShouldRecordAuditLog_OnSuccess()
    {
        var handler = CreateHandler();

        await handler.Handle(ValidCommand(), CancellationToken.None);

        _auditLogRepository.Received(1).Add(Arg.Is<McpAuditLog>(a => a.Action == McpAuditAction.ServerRegistered && a.UserId == AdminId));
    }
}
