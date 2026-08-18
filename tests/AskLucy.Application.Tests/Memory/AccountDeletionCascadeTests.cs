using AskLucy.Application.Abstractions;
using AskLucy.Application.Users.Commands.DeleteMyAccount;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Memory;

/// <summary>
/// tasks.md T102b (FR-026, research.md Decision 19, added during `/speckit-analyze` remediation
/// finding C2) — deleting a user account anonymizes their `MemoryAuditLog`/`MemoryNotification`
/// rows before the account itself is hard-deleted (never after — the row's own `UserId` is needed
/// to find it). The actual `Memory`/`Project`/`MemoryPreference`/`MemoryCategoryPreference` purge
/// happens for free via the existing `ApplicationUser`-rooted FK cascade (constitution §5), so it
/// isn't re-verified here — that's `IIdentityService.DeleteAsync`'s (and SQL Server's) job, not
/// this handler's.
/// </summary>
public sealed class AccountDeletionCascadeTests
{
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly IMemoryAuditLogRepository _auditLogRepository = Substitute.For<IMemoryAuditLogRepository>();
    private readonly IMemoryNotificationRepository _notificationRepository = Substitute.For<IMemoryNotificationRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly DeleteMyAccountCommandHandler _handler;
    private const string UserId = "user-1";

    public AccountDeletionCascadeTests()
    {
        _currentUser.UserId.Returns(UserId);
        _identityService.VerifyPasswordAsync(UserId, "correct-password", Arg.Any<CancellationToken>()).Returns(true);
        _handler = new DeleteMyAccountCommandHandler(_identityService, _auditLogRepository, _notificationRepository, _currentUser);
    }

    [Fact]
    public async Task Handle_ShouldAnonymizeMemoryAuditLogsAndNotifications_BeforeHardDeletingTheAccount()
    {
        var callOrder = new List<string>();
        _auditLogRepository.AnonymizeUserAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => callOrder.Add("anonymize-audit-logs"));
        _notificationRepository.AnonymizeUserAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => callOrder.Add("anonymize-notifications"));
        _identityService.DeleteAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => callOrder.Add("delete-account"));

        var result = await _handler.Handle(new DeleteMyAccountCommand("correct-password"), CancellationToken.None);

        result.Status.Should().Be(IdentityResultStatus.Success);
        callOrder.Should().Equal("anonymize-audit-logs", "anonymize-notifications", "delete-account");
    }

    [Fact]
    public async Task Handle_ShouldNeverAnonymizeOrDeleteAnything_WhenThePasswordIsWrong()
    {
        _identityService.VerifyPasswordAsync(UserId, "wrong-password", Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.Handle(new DeleteMyAccountCommand("wrong-password"), CancellationToken.None);

        result.Status.Should().Be(IdentityResultStatus.Failed);
        await _auditLogRepository.DidNotReceive().AnonymizeUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notificationRepository.DidNotReceive().AnonymizeUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _identityService.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
