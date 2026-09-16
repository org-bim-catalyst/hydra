using AskLucy.Domain.Authorization;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Authorization;

public sealed class RoleNameTests
{
    [Fact]
    public void From_ShouldTrimWhitespace()
    {
        var name = RoleName.From("  CustomRole  ");
        name.Value.Should().Be("CustomRole");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void From_ShouldThrow_WhenNameIsBlankOrWhitespace(string value)
    {
        var act = () => RoleName.From(value);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void From_ShouldThrow_WhenNameTooShort()
    {
        var act = () => RoleName.From("A");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void From_ShouldAcceptTwoCharName()
    {
        var name = RoleName.From("AB");
        name.Value.Should().Be("AB");
    }

    [Fact]
    public void From_ShouldAccept50CharName()
    {
        var name = RoleName.From(new string('X', 50));
        name.Value.Length.Should().Be(50);
    }

    [Fact]
    public void From_ShouldThrow_WhenNameTooLong()
    {
        var act = () => RoleName.From(new string('X', 51));
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("SUPER USER")]
    [InlineData("REGULAR")]
    [InlineData("No Role")]
    public void From_ShouldThrow_WhenNameIsReserved(string value)
    {
        var act = () => RoleName.From(value);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsNameReserved_ShouldBeTrueForReservedNames()
    {
        foreach (var name in new[] { "Administrator", "SUPER USER", "Regular", "NO role" })
        {
            RoleName.IsNameReserved(name).Should().BeTrue($"{name} should be reserved");
        }
    }

    [Fact]
    public void IsNameReserved_ShouldBeFalseForCustomNames()
    {
        RoleName.IsNameReserved("ContentEditor").Should().BeFalse();
        RoleName.IsNameReserved("ProjectViewer").Should().BeFalse();
    }

    [Fact]
    public void Equality_ShouldBeCaseInsensitive()
    {
        var a = RoleName.From("CustomRole");
        var b = RoleName.From("customrole");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        (a != b).Should().BeFalse();
    }

    [Fact]
    public void Equality_DifferentValues_ShouldNotBeEqual()
    {
        var a = RoleName.From("Admin");
        var b = RoleName.From("Editor");

        a.Should().NotBe(b);
    }

    [Fact]
    public void ToString_ReturnsTrimmedValue()
    {
        var name = RoleName.From("  Test  ");
        name.ToString().Should().Be("Test");
    }
}
