using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication.Commands.RequestAccountSupport;
using AskLucy.Application.Authentication.Commands.ResendEmailConfirmation;
using FluentAssertions;
using FluentValidation.TestHelper;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authentication;

/// <summary>
/// The two anonymous endpoints the sign-in page offers a stuck user. Both mirror
/// <see cref="RequestPasswordResetCommandHandlerTests"/>: the handler must enqueue and return,
/// because any account lookup on the request thread would make the neutral 202 measurably slower
/// for an address that exists.
/// </summary>
public sealed class AccountRecoveryCommandHandlerTests
{
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();

    [Fact]
    public async Task ResendEmailConfirmation_ShouldEnqueueTheSend_AndTouchNothingElse()
    {
        var handler = new ResendEmailConfirmationCommandHandler(_backgroundJobClient);

        await handler.Handle(new ResendEmailConfirmationCommand("user@example.com"), CancellationToken.None);

        _backgroundJobClient.Received(1).Create(
            Arg.Is<Job>(j => j != null && j.Method.Name == nameof(IAccountEmailJob.ResendConfirmationAsync)
                && j.Args.Contains("user@example.com")),
            Arg.Any<IState>());
    }

    [Fact]
    public async Task ResendEmailConfirmation_ShouldEnqueueTheSameWay_ForAnAddressThatCannotHaveAnAccount()
    {
        var handler = new ResendEmailConfirmationCommandHandler(_backgroundJobClient);

        await handler.Handle(new ResendEmailConfirmationCommand("nobody@example.invalid"), CancellationToken.None);

        _backgroundJobClient.Received(1).Create(
            Arg.Is<Job>(j => j != null && j.Method.Name == nameof(IAccountEmailJob.ResendConfirmationAsync)),
            Arg.Any<IState>());
    }

    [Fact]
    public async Task RequestAccountSupport_ShouldEnqueueTheRelay_WithTheSenderAndTheirMessage()
    {
        var handler = new RequestAccountSupportCommandHandler(_backgroundJobClient);

        await handler.Handle(
            new RequestAccountSupportCommand("locked@example.com", "Please unlock my account.", "203.0.113.5"),
            CancellationToken.None);

        _backgroundJobClient.Received(1).Create(
            Arg.Is<Job>(j => j != null && j.Method.Name == nameof(IAccountEmailJob.SendAccountSupportRequestAsync)
                && j.Args.Contains("locked@example.com")
                && j.Args.Contains("Please unlock my account.")),
            Arg.Any<IState>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    public void ResendEmailConfirmationValidator_ShouldRejectAnUnusableAddress(string email)
    {
        var result = new ResendEmailConfirmationCommandValidator().TestValidate(new ResendEmailConfirmationCommand(email));

        result.ShouldHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void RequestAccountSupportValidator_ShouldRejectAMessageBeyondTheRelayBound()
    {
        var command = new RequestAccountSupportCommand("locked@example.com", new string('x', 2001), null);

        var result = new RequestAccountSupportCommandValidator().TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.Message);
    }

    [Fact]
    public void RequestAccountSupportValidator_ShouldAcceptAMessageAtTheBound()
    {
        var command = new RequestAccountSupportCommand("locked@example.com", new string('x', 2000), null);

        new RequestAccountSupportCommandValidator().TestValidate(command).IsValid.Should().BeTrue();
    }
}
