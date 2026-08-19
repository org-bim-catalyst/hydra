using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Queries.GetUserVoicePreference;
using AskLucy.Domain.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/026-floating-chat-assistant FR-016/FR-017, contracts/voice-preference-api.md.</summary>
public sealed class GetUserVoicePreferenceQueryHandlerTests
{
    private readonly IUserVoicePreferenceRepository _preferences = Substitute.For<IUserVoicePreferenceRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly GetUserVoicePreferenceQueryHandler _handler;

    public GetUserVoicePreferenceQueryHandlerTests()
    {
        _currentUser.UserId.Returns("user-1");
        _handler = new GetUserVoicePreferenceQueryHandler(_preferences, _currentUser);
    }

    [Fact]
    public async Task Handle_ShouldReturnNullDefaultLanguage_WhenNoPreferenceRowExistsYet()
    {
        _preferences.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns((UserVoicePreference?)null);

        var result = await _handler.Handle(new GetUserVoicePreferenceQuery(), CancellationToken.None);

        result.DefaultLanguage.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnThePersistedDefaultLanguage()
    {
        var preference = UserVoicePreference.Create("user-1", "user-1");
        preference.SetDefaultLanguage("es", "user-1");
        _preferences.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(preference);

        var result = await _handler.Handle(new GetUserVoicePreferenceQuery(), CancellationToken.None);

        result.DefaultLanguage.Should().Be("es");
    }
}
