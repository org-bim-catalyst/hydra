using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Commands.UpdateToolSettings;
using AskLucy.Application.Agents.ToolSettings;
using AskLucy.Domain.Agents;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

/// <summary>
/// specs/083 — saving a tool's settings. A provider and a zoom that cannot work together would
/// make every capture fail with a refusal, so the pair is refused at the door.
/// </summary>
public sealed class UpdateToolSettingsCommandTests
{
    private readonly IToolSettingRepository _settings = Substitute.For<IToolSettingRepository>();
    private readonly ToolSettingCatalog _catalog = new();

    private UpdateToolSettingsCommandValidator Validator() => new(_catalog, _settings);

    private void Stored(params (string Key, string Value)[] rows) =>
        _settings.GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(rows.Select(r => ToolSetting.Create(ToolSettingCatalog.MapSnapshotToolName, r.Key, r.Value, "admin"))
                .FirstOrDefault(s => rows.Any(x => x.Key == s.Key))!);

    [Fact]
    public async Task ShouldRefuseAZoomTheChosenProviderCannotServe()
    {
        Stored((ToolSettingCatalog.DefaultSourceKey, "esri"));

        var result = await Validator().ValidateAsync(
            new UpdateToolSettingsCommand(
                ToolSettingCatalog.MapSnapshotToolName,
                new Dictionary<string, string> { [ToolSettingCatalog.DefaultZoomKey] = "20" }),
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse("Esri cannot be assembled past zoom 18");
    }

    [Fact]
    public async Task ShouldRefuseAZoomThatTheStoredProviderCannotServe()
    {
        Stored((ToolSettingCatalog.DefaultSourceKey, "esri"));

        var result = await Validator().ValidateAsync(
            new UpdateToolSettingsCommand(
                ToolSettingCatalog.MapSnapshotToolName,
                new Dictionary<string, string> { [ToolSettingCatalog.DefaultCropPixelsKey] = "48" }),
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse("the stored provider is Esri and the stored zoom is one it cannot serve");
    }

    [Fact]
    public async Task ShouldAcceptAPairTheProviderCanServe()
    {
        Stored((ToolSettingCatalog.DefaultSourceKey, "google"));

        var result = await Validator().ValidateAsync(
            new UpdateToolSettingsCommand(
                ToolSettingCatalog.MapSnapshotToolName,
                new Dictionary<string, string>
                {
                    [ToolSettingCatalog.DefaultSourceKey] = "google",
                    [ToolSettingCatalog.DefaultZoomKey] = "20",
                }),
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldRefuseAKeyTheToolDoesNotDeclare()
    {
        var result = await Validator().ValidateAsync(
            new UpdateToolSettingsCommand(
                ToolSettingCatalog.MapSnapshotToolName,
                new Dictionary<string, string> { ["notASetting"] = "x" }),
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
    }
}
