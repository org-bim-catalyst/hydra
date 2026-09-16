using AskLucy.Application.Common;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Common;

public sealed class BulkTargetValidatorTests
{
    private readonly BulkTargetValidator _validator = new();

    [Fact]
    public void Validate_ShouldPass_WhenIdsIsNonEmptyAndAllMatchingIsFalse()
    {
        var target = new BulkTarget(["u1", "u2"], false);

        _validator.Validate(target).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldPass_WhenAllMatchingIsTrueAndIdsIsNull()
    {
        var target = new BulkTarget(null, true);

        _validator.Validate(target).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldFail_WhenBothIdsAndAllMatchingAreSet()
    {
        var target = new BulkTarget(["u1"], true);

        _validator.Validate(target).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldFail_WhenNeitherIdsNorAllMatchingAreSet()
    {
        var target = new BulkTarget(null, false);

        _validator.Validate(target).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldFail_WhenIdsIsEmptyAndAllMatchingIsFalse()
    {
        var target = new BulkTarget([], false);

        _validator.Validate(target).IsValid.Should().BeFalse();
    }
}
