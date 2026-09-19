using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication.Commands.RequestPasswordReset;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authentication;

/// <summary>
/// specs/058-password-recovery T013. Guards the one property this handler exists for: it must do
/// no account-dependent work of its own, because anything it does on the request thread would show
/// up as a response-time difference between an address that has an account and one that does not
/// (FR-003).
/// </summary>
public sealed class RequestPasswordResetCommandHandlerTests
{
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();

    [Fact]
    public async Task Handle_ShouldEnqueueIssuance_AndTouchNothingElse()
    {
        var handler = new RequestPasswordResetCommandHandler(_backgroundJobClient);

        await handler.Handle(new RequestPasswordResetCommand("user@example.com", "203.0.113.5"), CancellationToken.None);

        _backgroundJobClient.Received(1).Create(
            Arg.Is<Job>(j => j != null && j.Method.Name == nameof(IPasswordResetIssuanceJob.IssueAsync)
                && j.Args.Contains("user@example.com")),
            Arg.Any<IState>());
    }

    [Fact]
    public async Task Handle_ShouldEnqueueTheSameWay_ForAnAddressThatCannotPossiblyHaveAnAccount()
    {
        var handler = new RequestPasswordResetCommandHandler(_backgroundJobClient);

        await handler.Handle(new RequestPasswordResetCommand("nobody@example.invalid", null), CancellationToken.None);

        _backgroundJobClient.Received(1).Create(
            Arg.Is<Job>(j => j != null && j.Method.Name == nameof(IPasswordResetIssuanceJob.IssueAsync)),
            Arg.Any<IState>());
    }
}
