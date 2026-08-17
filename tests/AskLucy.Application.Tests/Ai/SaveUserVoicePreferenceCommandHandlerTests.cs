using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.SaveUserVoicePreference;
using AskLucy.Domain.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/026-floating-chat-assistant FR-017, contracts/voice-preference-api.md —
/// `DefaultLanguage` round-trips through the same save path as the entity's other
/// fields.</summary>
public sealed class SaveUserVoicePreferenceCommandHandlerTests
{
    private readonly IUserVoicePreferenceRepository _preferences = Substitute.For<IUserVoicePreferenceRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly SaveUserVoicePreferenceCommandHandler _handler;

    public SaveUserVoicePreferenceCommandHandlerTests()
    {
        _currentUser.UserId.Returns("user-1");
        _handler = new SaveUserVoicePreferenceCommandHandler(_preferences, _unitOfWork, _currentUser);
    }

    [Fact]
    public async Task Handle_ShouldPersistAndReturnDefaultLanguage_WhenCreatingForTheFirstTime()
    {
        _preferences.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns((UserVoicePreference?)null);

        var result = await _handler.Handle(
            new SaveUserVoicePreferenceCommand("PushToTalk", false, null, null, null, null, null, "fr"),
            CancellationToken.None);

        result.DefaultLanguage.Should().Be("fr");
        _preferences.Received(1).Add(Arg.Is<UserVoicePreference>(p => p.DefaultLanguage == "fr"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldUpdateDefaultLanguage_OnAnExistingPreference()
    {
        var existing = UserVoicePreference.Create("user-1", "user-1");
        existing.SetDefaultLanguage("en", "user-1");
        _preferences.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _handler.Handle(
            new SaveUserVoicePreferenceCommand("PushToTalk", false, null, null, null, null, null, "de"),
            CancellationToken.None);

        result.DefaultLanguage.Should().Be("de");
        existing.DefaultLanguage.Should().Be("de");
    }

    [Fact]
    public async Task Handle_ShouldClearDefaultLanguage_WhenRequestSendsNull()
    {
        var existing = UserVoicePreference.Create("user-1", "user-1");
        existing.SetDefaultLanguage("en", "user-1");
        _preferences.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _handler.Handle(
            new SaveUserVoicePreferenceCommand("PushToTalk", false, null, null, null, null, null, null),
            CancellationToken.None);

        result.DefaultLanguage.Should().BeNull();
    }
}
