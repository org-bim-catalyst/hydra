using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Commands.RotateMcpServerCredential;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

/// <summary>spec.md FR-047 — in-place `CiphertextBlob` replacement, never delete+re-insert; no credential value ever leaves the handler in any form (response DTO or audit record).</summary>
public sealed class RotateMcpServerCredentialCommandHandlerTests
{
    private const string AdminId = "admin-1";

    private readonly IMcpServerRepository _serverRepository = Substitute.For<IMcpServerRepository>();
    private readonly IMcpAuditLogRepository _auditLogRepository = Substitute.For<IMcpAuditLogRepository>();
    private readonly IMcpCredentialProtector _credentialProtector = Substitute.For<IMcpCredentialProtector>();
    private readonly IMcpClientFactory _clientFactory = Substitute.For<IMcpClientFactory>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private RotateMcpServerCredentialCommandHandler CreateHandler() => new(_serverRepository, _auditLogRepository, _credentialProtector, _clientFactory, _unitOfWork, _currentUser);

    private static McpServer RegisterServer() => McpServer.Register(
        "Test", null, "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey, false, false, null, false, null, AdminId, 60);

    public RotateMcpServerCredentialCommandHandlerTests() => _currentUser.UserId.Returns(AdminId);

    [Fact]
    public async Task Handle_ShouldRotateInPlace_WhenACredentialAlreadyExists()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        var existingCredential = McpServerCredential.Create(server.Id, "old-ciphertext", AdminId);
        _serverRepository.GetCredentialAsync(server.Id, Arg.Any<CancellationToken>()).Returns(existingCredential);
        _credentialProtector.Protect("new-secret-value").Returns("new-ciphertext");
        var handler = CreateHandler();

        await handler.Handle(new RotateMcpServerCredentialCommand(server.Id, "new-secret-value"), CancellationToken.None);

        existingCredential.CiphertextBlob.Should().Be("new-ciphertext");
        existingCredential.RotatedByUserId.Should().Be(AdminId);
        _serverRepository.DidNotReceive().AddCredential(Arg.Any<McpServerCredential>());
        // FR-047 — the cached connection (if any) is invalidated so the next call reconnects
        // with the new credential, rather than silently continuing to use the old one.
        await _clientFactory.Received(1).InvalidateConnectionAsync(server.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldCreateACredentialRow_WhenNoneExistedYet()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetCredentialAsync(server.Id, Arg.Any<CancellationToken>()).Returns((McpServerCredential?)null);
        _credentialProtector.Protect("new-secret-value").Returns("new-ciphertext");
        var handler = CreateHandler();

        await handler.Handle(new RotateMcpServerCredentialCommand(server.Id, "new-secret-value"), CancellationToken.None);

        _serverRepository.Received(1).AddCredential(Arg.Is<McpServerCredential>(c => c!.CiphertextBlob == "new-ciphertext"));
    }

    [Fact]
    public async Task Handle_ShouldNeverIncludeTheCredentialValue_InTheResponseOrTheAuditRecord()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.GetCredentialAsync(server.Id, Arg.Any<CancellationToken>()).Returns((McpServerCredential?)null);
        _credentialProtector.Protect("super-secret-token-abc123").Returns("ciphertext-xyz");
        var handler = CreateHandler();

        var result = await handler.Handle(new RotateMcpServerCredentialCommand(server.Id, "super-secret-token-abc123"), CancellationToken.None);

        // McpServerDto has no credential-shaped field at all (verified structurally by every other
        // McpServerDto-returning handler test) — this asserts the audit record specifically.
        _auditLogRepository.Received(1).Add(Arg.Is<McpAuditLog>(a =>
            a!.Action == McpAuditAction.CredentialRotated &&
            !a.DetailsJson.Contains("super-secret-token-abc123") &&
            !a.DetailsJson.Contains("ciphertext-xyz")));
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenServerNotFound()
    {
        _serverRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((McpServer?)null);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new RotateMcpServerCredentialCommand(Guid.NewGuid(), "new-secret"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
