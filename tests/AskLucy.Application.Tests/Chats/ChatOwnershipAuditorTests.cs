using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.Chats;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Chats;

/// <summary>specs/079 — a non-owner is turned away with the same 404-style answer, and the denial is audited (constitution &#167;8).</summary>
public sealed class ChatOwnershipAuditorTests
{
    private readonly IRoleAuditLogRepository _auditLog = Substitute.For<IRoleAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private ChatOwnershipAuditor Create() => new(_auditLog, _unitOfWork);

    [Fact]
    public async Task EnsureOwnedByAsync_ShouldReturnTheChatAndWriteNothing_ForTheOwner()
    {
        var chat = UserChat.Create("Chat", "owner", null, "owner");

        var result = await Create().EnsureOwnedByAsync(chat, "owner", "edit-site-boundary", TestContext.Current.CancellationToken);

        result.Should().BeSameAs(chat);
        _auditLog.DidNotReceive().Add(Arg.Any<RoleAuditLog>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureOwnedByAsync_ShouldAuditAndThrowNotFound_ForANonOwner()
    {
        var chat = UserChat.Create("Chat", "owner", null, "owner");

        var act = () => Create().EnsureOwnedByAsync(chat, "intruder", "edit-site-boundary", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _auditLog.Received(1).Add(Arg.Is<RoleAuditLog>(e =>
            e!.Action == RoleAuditAction.AuthorizationDenied && e.ActorUserId == "intruder"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureOwnedByAsync_ShouldThrowNotFoundWithoutAuditing_ForAMissingChat()
    {
        var act = () => Create().EnsureOwnedByAsync(null, "anyone", "edit-site-boundary", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _auditLog.DidNotReceive().Add(Arg.Any<RoleAuditLog>());
    }
}
