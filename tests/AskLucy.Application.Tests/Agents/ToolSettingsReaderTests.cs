using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.ToolSettings;
using AskLucy.Application.MapCaptures;
using AskLucy.Domain.Agents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

/// <summary>
/// specs/083 — what the capture reads when the administrator has configured something, when they
/// have not, and when the settings could not be read at all.
/// </summary>
public sealed class ToolSettingsReaderTests
{
    private readonly IToolSettingRepository _settings = Substitute.For<IToolSettingRepository>();
    private readonly ToolSettingCatalog _catalog = new();

    private ToolSettingsReader Reader() =>
        new(_settings, _catalog, NullLogger<ToolSettingsReader>.Instance);

    private void Stored(params (string Key, string Value)[] rows) =>
        _settings.ListByToolAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(rows.Select(r => ToolSetting.Create(ToolSettingCatalog.MapSnapshotToolName, r.Key, r.Value, "admin")).ToList());

    [Fact]
    public async Task GetMapCaptureDefaultsAsync_ShouldFallBackToTheCatalogueDefaults_WhenNothingIsSaved()
    {
        Stored();

        var defaults = await Reader().GetMapCaptureDefaultsAsync(TestContext.Current.CancellationToken);

        defaults.Should().Be(MapCaptureDefaults.Default);
    }

    [Fact]
    public async Task GetMapCaptureDefaultsAsync_ShouldReturnTheSavedValues()
    {
        Stored(
            (ToolSettingCatalog.DefaultSourceKey, "google"),
            (ToolSettingCatalog.DefaultCropPixelsKey, "64"),
            (ToolSettingCatalog.DefaultZoomKey, "19"),
            (ToolSettingCatalog.EnhanceGoogleColourKey, "false"));

        var defaults = await Reader().GetMapCaptureDefaultsAsync(TestContext.Current.CancellationToken);

        defaults.Source.Should().Be(MapCaptureSource.Google);
        defaults.CropPixels.Should().Be(64);
        defaults.Zoom.Should().Be(19);
        defaults.EnhanceGoogleColour.Should().BeFalse();
    }

    [Fact]
    public async Task GetMapCaptureDefaultsAsync_ShouldIgnoreASavedValueThatIsNoLongerValid()
    {
        // A value the catalogue no longer accepts (a withdrawn choice, a number out of range)
        // must not reach the capture.
        Stored((ToolSettingCatalog.DefaultSourceKey, "someRetiredProvider"));

        var defaults = await Reader().GetMapCaptureDefaultsAsync(TestContext.Current.CancellationToken);

        defaults.Source.Should().Be(MapCaptureSource.Esri);
    }

    [Fact]
    public async Task GetMapCaptureDefaultsAsync_ShouldFallBackToTheDefaults_WhenTheSettingsCannotBeRead()
    {
        // A missing settings table must not break the capture itself.
        _settings.ListByToolAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ToolSetting>>(_ => throw new InvalidOperationException("no such table"));

        var defaults = await Reader().GetMapCaptureDefaultsAsync(TestContext.Current.CancellationToken);

        defaults.Should().Be(MapCaptureDefaults.Default);
    }

    [Fact]
    public void Catalog_ShouldDeclareTheMapSnapshotDefaults()
    {
        var definitions = _catalog.For(ToolSettingCatalog.MapSnapshotToolName);

        definitions.Select(d => d.Key).Should().BeEquivalentTo(
        [
            ToolSettingCatalog.DefaultSourceKey,
            ToolSettingCatalog.DefaultCropPixelsKey,
            ToolSettingCatalog.DefaultZoomKey,
            ToolSettingCatalog.EnhanceGoogleColourKey,
        ]);
        _catalog.ConfigurableTools.Should().Contain(ToolSettingCatalog.MapSnapshotToolName);
    }

    [Fact]
    public void Catalog_ShouldScopeTheZoomLevelsToTheProviderThatCanServeThem()
    {
        var zoom = _catalog.Find(ToolSettingCatalog.MapSnapshotToolName, ToolSettingCatalog.DefaultZoomKey)!;

        zoom.ScopedBy.Should().Be(ToolSettingCatalog.DefaultSourceKey);
        zoom.Choices.Should().Contain(o => o.Scope == "esri" && o.Value == "18");
        zoom.Choices.Should().Contain(o => o.Scope == "google" && o.Value == "19");
        zoom.Choices.Should().Contain(o => o.Scope == "google" && o.Value == "21");
        zoom.Choices.Should().NotContain(o => o.Scope == "esri" && o.Value == "19");
        zoom.Choices.Should().NotContain(o => o.Scope == "google" && o.Value == "18");
    }

    [Fact]
    public void Catalog_ShouldScopeTheColourCorrectionToGoogleAlone()
    {
        var colour = _catalog.Find(ToolSettingCatalog.MapSnapshotToolName, ToolSettingCatalog.EnhanceGoogleColourKey)!;

        colour.ScopedBy.Should().Be(ToolSettingCatalog.DefaultSourceKey);
        colour.ScopeValue.Should().Be("google");
    }

    [Theory]
    [InlineData("esri", "14", true)]
    [InlineData("esri", "18", true)]
    [InlineData("esri", "19", false)]
    [InlineData("esri", "21", false)]
    [InlineData("google", "19", true)]
    [InlineData("google", "21", true)]
    [InlineData("google", "13", false)]
    [InlineData("esri", "auto", true)]
    [InlineData("google", "auto", true)]
    public void IsZoomServableBy_ShouldFollowWhatEachProviderCanActuallyServe(string source, string zoom, bool expected)
    {
        ToolSettingCatalog.IsZoomServableBy(source, zoom).Should().Be(expected);
    }

    [Fact]
    public async Task GetMapCaptureDefaultsAsync_ShouldDropAZoomTheChosenProviderCannotServe()
    {
        // A pair stored before the save-time rule existed must not make every capture fail.
        Stored(
            (ToolSettingCatalog.DefaultSourceKey, "esri"),
            (ToolSettingCatalog.DefaultZoomKey, "20"));

        var defaults = await Reader().GetMapCaptureDefaultsAsync(TestContext.Current.CancellationToken);

        defaults.Source.Should().Be(MapCaptureSource.Esri);
        defaults.Zoom.Should().BeNull("the view size decides the zoom again");
    }

    [Theory]
    [InlineData(ToolSettingCatalog.DefaultSourceKey, "esri", true)]
    [InlineData(ToolSettingCatalog.DefaultSourceKey, "google", true)]
    [InlineData(ToolSettingCatalog.DefaultSourceKey, "bing", false)]
    [InlineData(ToolSettingCatalog.DefaultCropPixelsKey, "32", true)]
    [InlineData(ToolSettingCatalog.DefaultCropPixelsKey, "500", false)]
    [InlineData(ToolSettingCatalog.DefaultCropPixelsKey, "-1", false)]
    [InlineData(ToolSettingCatalog.EnhanceGoogleColourKey, "true", true)]
    [InlineData(ToolSettingCatalog.EnhanceGoogleColourKey, "yes", false)]
    public void Catalog_ShouldAcceptOnlyTheValuesItsTypeAllows(string key, string value, bool expected)
    {
        var definition = _catalog.Find(ToolSettingCatalog.MapSnapshotToolName, key)!;

        ToolSettingCatalog.IsValidValue(definition, value).Should().Be(expected);
    }
}
