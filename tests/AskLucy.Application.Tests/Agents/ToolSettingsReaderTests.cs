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
