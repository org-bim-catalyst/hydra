using AskLucy.Application.Abstractions;
using AskLucy.Application.Panels.Commands.SaveUserPanelPreference;
using AskLucy.Domain.Panels;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Panels;

public sealed class SaveUserPanelPreferenceCommandHandlerTests
{
    private readonly IUserPanelPreferenceRepository _preferences = Substitute.For<IUserPanelPreferenceRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly SaveUserPanelPreferenceCommandHandler _handler;

    public SaveUserPanelPreferenceCommandHandlerTests()
    {
        _currentUser.UserId.Returns("user-1");
        _handler = new SaveUserPanelPreferenceCommandHandler(_preferences, _unitOfWork, _currentUser);
    }

    [Fact]
    public async Task Handle_ShouldCreateAndPersist_WhenNoPreferenceRowExistsYet()
    {
        _preferences.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns((UserPanelPreference?)null);

        var result = await _handler.Handle(new SaveUserPanelPreferenceCommand(60), CancellationToken.None);

        result.OpacityPercent.Should().Be(60);
        _preferences.Received(1).Add(Arg.Is<UserPanelPreference>(p => p != null && p.OpacityPercent == 60));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldUpdateOpacity_OnAnExistingPreference()
    {
        var existing = UserPanelPreference.Create("user-1", "user-1");
        existing.SetOpacityPercent(70, "user-1");
        _preferences.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _handler.Handle(new SaveUserPanelPreferenceCommand(50), CancellationToken.None);

        result.OpacityPercent.Should().Be(50);
        existing.OpacityPercent.Should().Be(50);
        _preferences.DidNotReceive().Add(Arg.Any<UserPanelPreference>());
    }
}
