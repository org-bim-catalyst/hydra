using AskLucy.Application.Abstractions;
using AskLucy.Application.Panels.Queries.GetUserPanelPreference;
using AskLucy.Domain.Panels;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Panels;

/// <summary>specs/028-ai-floating-panels contracts/panel-preferences-api.md.</summary>
public sealed class GetUserPanelPreferenceQueryHandlerTests
{
    private readonly IUserPanelPreferenceRepository _preferences = Substitute.For<IUserPanelPreferenceRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly GetUserPanelPreferenceQueryHandler _handler;

    public GetUserPanelPreferenceQueryHandlerTests()
    {
        _currentUser.UserId.Returns("user-1");
        _handler = new GetUserPanelPreferenceQueryHandler(_preferences, _currentUser);
    }

    [Fact]
    public async Task Handle_ShouldReturnTheDefaultOpacity_WhenNoPreferenceRowExistsYet()
    {
        _preferences.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns((UserPanelPreference?)null);

        var result = await _handler.Handle(new GetUserPanelPreferenceQuery(), CancellationToken.None);

        result.OpacityPercent.Should().Be(UserPanelPreference.DefaultOpacityPercent);
    }

    [Fact]
    public async Task Handle_ShouldReturnThePersistedOpacity()
    {
        var preference = UserPanelPreference.Create("user-1", "user-1");
        preference.SetOpacityPercent(55, "user-1");
        _preferences.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(preference);

        var result = await _handler.Handle(new GetUserPanelPreferenceQuery(), CancellationToken.None);

        result.OpacityPercent.Should().Be(55);
    }
}
