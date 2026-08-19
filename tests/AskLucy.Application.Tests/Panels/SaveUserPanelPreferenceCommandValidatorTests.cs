using AskLucy.Application.Panels.Commands.SaveUserPanelPreference;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Panels;

/// <summary>specs/028-ai-floating-panels Clarifications Q4 — bounded [40, 100], rejected outright,
/// never silently coerced (constitution §2.VIII).</summary>
public sealed class SaveUserPanelPreferenceCommandValidatorTests
{
    private readonly SaveUserPanelPreferenceCommandValidator _validator = new();

    [Theory]
    [InlineData(40)]
    [InlineData(85)]
    [InlineData(100)]
    public void Validate_ShouldPass_ForValuesWithinRange(int opacityPercent)
    {
        var result = _validator.Validate(new SaveUserPanelPreferenceCommand(opacityPercent));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(39)]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(101)]
    [InlineData(1000)]
    public void Validate_ShouldFail_ForValuesOutsideRange(int opacityPercent)
    {
        var result = _validator.Validate(new SaveUserPanelPreferenceCommand(opacityPercent));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(SaveUserPanelPreferenceCommand.OpacityPercent));
    }
}
