using System.Security.Claims;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication.Commands.ExternalLogin;
using AskLucy.Application.Users;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authentication;

public sealed class ProcessExternalLoginCallbackCommandHandlerTests
{
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly IExternalLoginCodeStore _codeStore = Substitute.For<IExternalLoginCodeStore>();
    private readonly IUserProfileRepository _userProfileRepository = Substitute.For<IUserProfileRepository>();
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();
    private readonly ProcessExternalLoginCallbackCommandHandler _handler;

    public ProcessExternalLoginCallbackCommandHandlerTests()
    {
        _handler = new ProcessExternalLoginCallbackCommandHandler(
            _identityService, _codeStore, _userProfileRepository, _backgroundJobClient);
    }

    [Fact]
    public async Task Handle_ShouldIssueCompletionCode_WhenResolutionSucceeds()
    {
        _identityService.ResolveExternalLoginAsync("Google", "provider-key-1", "user@example.com", true, null, Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success, "user-1", [new Claim(ClaimTypes.NameIdentifier, "user-1")]));
        _codeStore.Issue("user-1", Arg.Any<TimeSpan>()).Returns("completion-code");

        var code = await _handler.Handle(
            new ProcessExternalLoginCallbackCommand("Google", "provider-key-1", "user@example.com", true, null, null, null, null),
            CancellationToken.None);

        code.Should().Be("completion-code");
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenResolutionFails()
    {
        _identityService.ResolveExternalLoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Failed, Errors: ["nope"]));

        var code = await _handler.Handle(
            new ProcessExternalLoginCallbackCommand("Google", "provider-key-1", "user@example.com", true, null, null, null, null),
            CancellationToken.None);

        code.Should().BeNull();
        _codeStore.DidNotReceiveWithAnyArgs().Issue(default!, default);
    }

    [Fact]
    public async Task Handle_ShouldPassLinkToUserId_WhenLinking()
    {
        _identityService.ResolveExternalLoginAsync("Facebook", "provider-key-2", null, false, "existing-user", Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success, "existing-user", []));
        _codeStore.Issue("existing-user", Arg.Any<TimeSpan>()).Returns("link-code");

        var code = await _handler.Handle(
            new ProcessExternalLoginCallbackCommand("Facebook", "provider-key-2", null, false, "existing-user", null, null, null),
            CancellationToken.None);

        code.Should().Be("link-code");
    }

    [Fact]
    public async Task Handle_ShouldSyncNameAndEnqueuePictureJob_WhenAllClaimsArePresentOnNewAccount()
    {
        _identityService.ResolveExternalLoginAsync("Google", "provider-key-3", "new@example.com", true, null, Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success, "user-3", []));
        _userProfileRepository.GetByIdAsync("user-3", Arg.Any<CancellationToken>()).Returns((UserProfileDto?)null);
        _codeStore.Issue("user-3", Arg.Any<TimeSpan>()).Returns("completion-code");

        var code = await _handler.Handle(
            new ProcessExternalLoginCallbackCommand(
                "Google", "provider-key-3", "new@example.com", true, null, "Ada", "Lovelace", "https://lh3.googleusercontent.com/a/photo"),
            CancellationToken.None);

        code.Should().Be("completion-code");
        await _userProfileRepository.Received(1).UpdateAsync("user-3", "Ada", "Lovelace", Arg.Any<CancellationToken>());
        _backgroundJobClient.Received(1).Create(
            Arg.Is<Job>(j => j != null
                && j.Method.Name == nameof(IExternalProfilePictureSyncJob.SyncAsync)
                && j.Args.Contains("user-3")
                && j.Args.Contains("https://lh3.googleusercontent.com/a/photo")),
            Arg.Any<IState>());
    }

    [Fact]
    public async Task Handle_ShouldNotEnqueuePictureJob_WhenPictureUrlIsAbsent()
    {
        _identityService.ResolveExternalLoginAsync("Google", "provider-key-4", "new2@example.com", true, null, Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success, "user-4", []));
        _userProfileRepository.GetByIdAsync("user-4", Arg.Any<CancellationToken>()).Returns((UserProfileDto?)null);
        _codeStore.Issue("user-4", Arg.Any<TimeSpan>()).Returns("completion-code");

        var code = await _handler.Handle(
            new ProcessExternalLoginCallbackCommand(
                "Google", "provider-key-4", "new2@example.com", true, null, "Grace", "Hopper", null),
            CancellationToken.None);

        code.Should().Be("completion-code");
        await _userProfileRepository.Received(1).UpdateAsync("user-4", "Grace", "Hopper", Arg.Any<CancellationToken>());
        _backgroundJobClient.DidNotReceiveWithAnyArgs().Create(default!, default!);
    }

    [Fact]
    public async Task Handle_ShouldSyncNameAndEnqueuePictureJob_WhenLinkingAnAdditionalProvider()
    {
        _identityService.ResolveExternalLoginAsync("Facebook", "provider-key-5", null, false, "existing-user-2", Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success, "existing-user-2", []));
        _userProfileRepository.GetByIdAsync("existing-user-2", Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto("existing-user-2", "existing2@example.com", null, "OldLast", default, false, null));
        _codeStore.Issue("existing-user-2", Arg.Any<TimeSpan>()).Returns("link-code");

        var code = await _handler.Handle(
            new ProcessExternalLoginCallbackCommand(
                "Facebook", "provider-key-5", null, false, "existing-user-2", "NewFirst", null,
                "https://platform-lookaside.fbsbx.com/photo"),
            CancellationToken.None);

        code.Should().Be("link-code");
        await _userProfileRepository.Received(1).UpdateAsync("existing-user-2", "NewFirst", "OldLast", Arg.Any<CancellationToken>());
        _backgroundJobClient.Received(1).Create(
            Arg.Is<Job>(j => j != null
                && j.Method.Name == nameof(IExternalProfilePictureSyncJob.SyncAsync)
                && j.Args.Contains("existing-user-2")),
            Arg.Any<IState>());
    }

    [Fact]
    public async Task Handle_ShouldPopulateAllFields_WhenExistingAccountHasNoNameOrAvatarYet()
    {
        _identityService.ResolveExternalLoginAsync("Google", "provider-key-6", "existing3@example.com", true, null, Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success, "existing-user-3", []));
        _userProfileRepository.GetByIdAsync("existing-user-3", Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto("existing-user-3", "existing3@example.com", null, null, default, false, null));
        _codeStore.Issue("existing-user-3", Arg.Any<TimeSpan>()).Returns("heal-code");

        var code = await _handler.Handle(
            new ProcessExternalLoginCallbackCommand(
                "Google", "provider-key-6", "existing3@example.com", true, null, "Marie", "Curie",
                "https://lh3.googleusercontent.com/a/photo2"),
            CancellationToken.None);

        code.Should().Be("heal-code");
        await _userProfileRepository.Received(1).UpdateAsync("existing-user-3", "Marie", "Curie", Arg.Any<CancellationToken>());
        _backgroundJobClient.Received(1).Create(
            Arg.Is<Job>(j => j != null
                && j.Method.Name == nameof(IExternalProfilePictureSyncJob.SyncAsync)
                && j.Args.Contains("existing-user-3")),
            Arg.Any<IState>());
    }

    [Fact]
    public async Task Handle_ShouldOverwritePreviouslySetNames_WhenTheProviderClaimsDiffer()
    {
        _identityService.ResolveExternalLoginAsync("Google", "provider-key-7", "existing4@example.com", true, null, Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success, "existing-user-4", []));
        _userProfileRepository.GetByIdAsync("existing-user-4", Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto("existing-user-4", "existing4@example.com", "ManuallyEditedFirst", "ManuallyEditedLast", default, false, "old-avatar.jpg"));
        _codeStore.Issue("existing-user-4", Arg.Any<TimeSpan>()).Returns("overwrite-code");

        var code = await _handler.Handle(
            new ProcessExternalLoginCallbackCommand(
                "Google", "provider-key-7", "existing4@example.com", true, null, "ProviderFirst", "ProviderLast", null),
            CancellationToken.None);

        code.Should().Be("overwrite-code");
        await _userProfileRepository.Received(1).UpdateAsync("existing-user-4", "ProviderFirst", "ProviderLast", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldLeaveTheOtherFieldUnchanged_WhenOnlyOneNameClaimIsPresentOnThisSignIn()
    {
        _identityService.ResolveExternalLoginAsync("Google", "provider-key-8", "existing5@example.com", true, null, Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success, "existing-user-5", []));
        _userProfileRepository.GetByIdAsync("existing-user-5", Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto("existing-user-5", "existing5@example.com", "StoredFirst", "StoredLast", default, false, null));
        _codeStore.Issue("existing-user-5", Arg.Any<TimeSpan>()).Returns("merge-code");

        var code = await _handler.Handle(
            new ProcessExternalLoginCallbackCommand(
                "Google", "provider-key-8", "existing5@example.com", true, null, "NewFirstOnly", null, null),
            CancellationToken.None);

        code.Should().Be("merge-code");
        await _userProfileRepository.Received(1).UpdateAsync("existing-user-5", "NewFirstOnly", "StoredLast", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldUpdateAgainToTheNewValues_WhenASecondSignInHasDifferentNameClaims()
    {
        _identityService.ResolveExternalLoginAsync("Google", "provider-key-9", "existing6@example.com", true, null, Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success, "existing-user-6", []));
        _codeStore.Issue("existing-user-6", Arg.Any<TimeSpan>()).Returns("first-code", "second-code");

        _userProfileRepository.GetByIdAsync("existing-user-6", Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto("existing-user-6", "existing6@example.com", "FirstNameOne", "LastNameOne", default, false, null));
        await _handler.Handle(
            new ProcessExternalLoginCallbackCommand(
                "Google", "provider-key-9", "existing6@example.com", true, null, "FirstNameOne", "LastNameOne", null),
            CancellationToken.None);

        _userProfileRepository.GetByIdAsync("existing-user-6", Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto("existing-user-6", "existing6@example.com", "FirstNameOne", "LastNameOne", default, false, null));
        var secondCode = await _handler.Handle(
            new ProcessExternalLoginCallbackCommand(
                "Google", "provider-key-9", "existing6@example.com", true, null, "FirstNameTwo", "LastNameTwo", null),
            CancellationToken.None);

        secondCode.Should().Be("second-code");
        await _userProfileRepository.Received(1).UpdateAsync("existing-user-6", "FirstNameTwo", "LastNameTwo", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldEnqueuePictureJobAgain_WhenASecondSignInHasAChangedPictureUrl()
    {
        _identityService.ResolveExternalLoginAsync("Google", "provider-key-10", "existing7@example.com", true, null, Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success, "existing-user-7", []));
        _codeStore.Issue("existing-user-7", Arg.Any<TimeSpan>()).Returns("first-code", "second-code");

        _userProfileRepository.GetByIdAsync("existing-user-7", Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto("existing-user-7", "existing7@example.com", "First", "Last", default, false, null));
        await _handler.Handle(
            new ProcessExternalLoginCallbackCommand(
                "Google", "provider-key-10", "existing7@example.com", true, null, "First", "Last",
                "https://lh3.googleusercontent.com/a/photo-one"),
            CancellationToken.None);

        _userProfileRepository.GetByIdAsync("existing-user-7", Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto("existing-user-7", "existing7@example.com", "First", "Last", default, false, "stored-photo-one.jpg"));
        await _handler.Handle(
            new ProcessExternalLoginCallbackCommand(
                "Google", "provider-key-10", "existing7@example.com", true, null, "First", "Last",
                "https://lh3.googleusercontent.com/a/photo-two"),
            CancellationToken.None);

        _backgroundJobClient.Received(1).Create(
            Arg.Is<Job>(j => j != null
                && j.Method.Name == nameof(IExternalProfilePictureSyncJob.SyncAsync)
                && j.Args.Contains("https://lh3.googleusercontent.com/a/photo-one")),
            Arg.Any<IState>());
        _backgroundJobClient.Received(1).Create(
            Arg.Is<Job>(j => j != null
                && j.Method.Name == nameof(IExternalProfilePictureSyncJob.SyncAsync)
                && j.Args.Contains("https://lh3.googleusercontent.com/a/photo-two")),
            Arg.Any<IState>());
    }
}
