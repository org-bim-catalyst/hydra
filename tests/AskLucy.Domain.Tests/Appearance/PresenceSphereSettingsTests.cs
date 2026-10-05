using AskLucy.Domain.Appearance;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Appearance;

public sealed class PresenceSphereSettingsTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Defaults_ShouldBeTheLookTheSphereHadBeforeItWasAdjustable()
    {
        PresenceSphereSettings.DefaultDotSizeMultiplier.Should().Be(1.00m);
        PresenceSphereSettings.DefaultCardFillPercent.Should().Be(75);
        PresenceSphereSettings.DefaultZoomEnabled.Should().BeFalse();
    }

    [Fact]
    public void Create_ShouldStoreTheValuesAndWhoSavedThemAndWhen()
    {
        var settings = PresenceSphereSettings.Create(0.8m, 70, true, "admin-1", Now);

        settings.Id.Should().Be(PresenceSphereSettings.SingletonId);
        settings.DotSizeMultiplier.Should().Be(0.8m);
        settings.CardFillPercent.Should().Be(70);
        settings.ZoomEnabled.Should().BeTrue();
        settings.CreatedBy.Should().Be("admin-1");
        settings.ModifiedBy.Should().Be("admin-1");
        settings.ModifiedAtUtc.Should().Be(Now);
    }

    [Theory]
    [InlineData(0.25, 40)]
    [InlineData(2.00, 95)]
    public void Update_ShouldAcceptTheValuesAtEachEnd(double dotSize, int fill)
    {
        var settings = PresenceSphereSettings.Create(1.00m, 75, false, "admin-1", Now);

        settings.Update((decimal)dotSize, fill, true, "admin-2", Now.AddMinutes(1));

        settings.DotSizeMultiplier.Should().Be((decimal)dotSize);
        settings.CardFillPercent.Should().Be(fill);
        settings.ModifiedBy.Should().Be("admin-2");
    }

    [Theory]
    [InlineData(0.24)]
    [InlineData(2.01)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Update_ShouldRefuseADotSizeOutsideItsRange_AndChangeNothing(double dotSize)
    {
        var settings = PresenceSphereSettings.Create(1.00m, 75, false, "admin-1", Now);

        var act = () => settings.Update((decimal)dotSize, 75, true, "admin-2", Now.AddMinutes(1));

        act.Should().Throw<ArgumentOutOfRangeException>();
        settings.DotSizeMultiplier.Should().Be(1.00m);
        settings.ZoomEnabled.Should().BeFalse();
        settings.ModifiedBy.Should().Be("admin-1");
    }

    [Theory]
    [InlineData(39)]
    [InlineData(96)]
    public void Update_ShouldRefuseAFillOutsideItsRange_AndChangeNothing(int fill)
    {
        var settings = PresenceSphereSettings.Create(1.00m, 75, false, "admin-1", Now);

        var act = () => settings.Update(1.00m, fill, false, "admin-2", Now.AddMinutes(1));

        act.Should().Throw<ArgumentOutOfRangeException>();
        settings.CardFillPercent.Should().Be(75);
    }
}
