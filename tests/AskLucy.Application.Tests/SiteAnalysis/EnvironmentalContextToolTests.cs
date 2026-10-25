using System.Text.Json;
using System.Text.RegularExpressions;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Application.SiteAnalysis.Providers;
using AskLucy.Application.SiteAnalysis.Tools;
using AskLucy.Domain.SiteAnalysis;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using SiteAnalysisAggregate = AskLucy.Domain.SiteAnalysis.SiteAnalysis;

namespace AskLucy.Application.Tests.SiteAnalysis;

/// <summary>
/// specs/088-environmental-context-specialist — the environmental specialist's delivery, rendering,
/// confidence, solar reference, and failure behaviour. Mirrors
/// <see cref="SiteAnalysisResultRelayTests"/>'s substitution style and
/// <see cref="SiteAnalysisProgressiveDeliveryTests"/>'s use of the real relay contract.
/// </summary>
public sealed class EnvironmentalContextToolTests
{
    private const string UserId = "user-1";
    private const string SiteName = "Al Barsha South";
    private const double Latitude = 25.09;
    private const double Longitude = 55.20;
    private const string FloodReason = "No flood data source is configured.";
    private const string ClimateReason = "No climate data source is configured.";

    private static readonly string[] AllowedKinds = ["heading", "text", "keyValue", "divider"];

    private readonly Guid _analysisId = Guid.CreateVersion7();

    private readonly ISiteAnalysisRepository _repository = Substitute.For<ISiteAnalysisRepository>();
    private readonly IFloodDataProvider _flood = Substitute.For<IFloodDataProvider>();
    private readonly IClimateDataProvider _climate = Substitute.For<IClimateDataProvider>();
    private readonly ISiteAnalysisResultRelay _relay = Substitute.For<ISiteAnalysisResultRelay>();

    private SiteAnalysisResultMetadata? _reportedMetadata;
    private JsonDocument? _reportedContent;

    public EnvironmentalContextToolTests()
    {
        var analysis = SiteAnalysisAggregate.Create(UserId, Guid.NewGuid(), SiteName, Latitude, Longitude, null, 2, UserId);
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(analysis);

        _flood.ResolveAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(DataResolutionOutcome.Unavailable<FloodData>(FloodReason));
        _climate.ResolveAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(DataResolutionOutcome.Unavailable<ClimateData>(ClimateReason));

        _relay
            .ReportSuccessAsync(
                Arg.Any<Guid>(), Arg.Any<SiteAnalysisType>(), Arg.Any<SiteAnalysisResultMetadata>(),
                Arg.Any<JsonDocument>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _reportedMetadata = call.Arg<SiteAnalysisResultMetadata>();
                _reportedContent = JsonDocument.Parse(call.Arg<JsonDocument>().RootElement.GetRawText());
                return Task.CompletedTask;
            });
    }

    private SiteEnvironmentalContextTool BuildTool()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_repository);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new SiteEnvironmentalContextTool(
            scopeFactory, new SiteAnalysisContentComposer(), _relay, _flood, _climate);
    }

    private JsonDocument Input() =>
        JsonDocument.Parse(
            $$"""{"siteAnalysisId":"{{_analysisId}}","siteName":"Ignored name","siteLocation":"Ignored location"}""");

    private static AgentToolExecutionContext Context() =>
        new(Guid.NewGuid(), Guid.NewGuid(), UserId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    // ---------- delivery and rendering ----------

    [Fact]
    public async Task ExecuteAsync_ShouldReportSuccess_WhenBothProvidersAreUnavailable()
    {
        var tool = BuildTool();

        var result = await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("reported").GetBoolean().Should().BeTrue();

        await _relay.Received(1).ReportSuccessAsync(
            _analysisId, SiteAnalysisType.EnvironmentalContext, Arg.Any<SiteAnalysisResultMetadata>(),
            Arg.Any<JsonDocument>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRenderEveryFigureAsUnavailable_WithItsReason()
    {
        var tool = BuildTool();
        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        var values = KeyValues(_reportedContent!);

        values["Flood zone"].Should().Be($"No data source available — {FloodReason}");
        values["Annual exceedance probability"].Should().Be($"No data source available — {FloodReason}");
        values["Prevailing wind direction"].Should().Be($"No data source available — {ClimateReason}");
        values["Average wind speed"].Should().Be($"No data source available — {ClimateReason}");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldOmitNoFigure()
    {
        var tool = BuildTool();
        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        KeyValues(_reportedContent!).Keys.Should().Contain(
            ["Flood zone", "Annual exceedance probability", "Prevailing wind direction", "Average wind speed"]);
    }

    [Theory]
    [InlineData(DataResolutionOutcomeType.NoCandidates, "No data available for this location")]
    [InlineData(DataResolutionOutcomeType.Ambiguous, "Conflicting data for this location")]
    [InlineData(DataResolutionOutcomeType.Unavailable, "No data source available")]
    public async Task ExecuteAsync_ShouldRenderEachNonConfirmedOutcomeDistinctly(
        DataResolutionOutcomeType outcomeType, string expectedText)
    {
        _flood.ResolveAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(new DataResolutionOutcome<FloodData>(outcomeType, null, "because"));

        var tool = BuildTool();
        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        var values = KeyValues(_reportedContent!);
        values["Flood zone"].Should().Be($"{expectedText} — because");
        values["Annual exceedance probability"].Should().Be($"{expectedText} — because");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportNotReported_WhenAConfirmedFieldIsNull()
    {
        _flood.ResolveAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(DataResolutionOutcome.Confirmed(new FloodData(null, 1.0)));

        var tool = BuildTool();
        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        var values = KeyValues(_reportedContent!);
        values["Flood zone"].Should().Be("Not reported by the source");
        values["Annual exceedance probability"].Should().Be("1%");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportAnUnusableValue_WhenAFigureIsOutOfRange()
    {
        _climate.ResolveAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(DataResolutionOutcome.Confirmed(new ClimateData(400, 5.0)));

        var tool = BuildTool();
        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        var values = KeyValues(_reportedContent!);
        values["Prevailing wind direction"].Should().Be("The source returned an unusable value");
        values["Average wind speed"].Should().Be("5 m/s");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRenderConfirmedValues_WithTheirUnits()
    {
        _flood.ResolveAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(DataResolutionOutcome.Confirmed(new FloodData("AE", 1.0)));
        _climate.ResolveAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(DataResolutionOutcome.Confirmed(new ClimateData(270, 5.2)));

        var tool = BuildTool();
        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        var values = KeyValues(_reportedContent!);
        values["Flood zone"].Should().Be("AE");
        values["Annual exceedance probability"].Should().Be("1%");
        values["Prevailing wind direction"].Should().Be("270°");
        values["Average wind speed"].Should().Be("5.2 m/s");
    }

    // ---------- confidence and provenance ----------

    [Fact]
    public async Task ExecuteAsync_ShouldReportLowConfidence_WhenNothingIsGrounded()
    {
        var tool = BuildTool();
        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        _reportedMetadata!.ConfidenceLevel.Should().Be(SiteAnalysisConfidenceLevel.Low);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportMediumConfidence_WhenGroundingIsPartial()
    {
        _flood.ResolveAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(DataResolutionOutcome.Confirmed(new FloodData("AE", 1.0)));

        var tool = BuildTool();
        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        _reportedMetadata!.ConfidenceLevel.Should().Be(SiteAnalysisConfidenceLevel.Medium);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportHighConfidence_WhenEveryFigureIsGrounded()
    {
        _flood.ResolveAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(DataResolutionOutcome.Confirmed(new FloodData("AE", 1.0)));
        _climate.ResolveAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(DataResolutionOutcome.Confirmed(new ClimateData(270, 5.2)));

        var tool = BuildTool();
        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        _reportedMetadata!.ConfidenceLevel.Should().Be(SiteAnalysisConfidenceLevel.High);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNameNoSource_WhenNothingIsGrounded()
    {
        var tool = BuildTool();
        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        _reportedMetadata!.DataSource.Should().Be("no data source configured");
        _reportedMetadata.AnalysisType.Should().Be(SiteAnalysisType.EnvironmentalContext);
        _reportedMetadata.SiteName.Should().Be(SiteName);
    }

    // ---------- solar reference and scope statement ----------

    [Fact]
    public async Task ExecuteAsync_ShouldReferenceTheViewersSolarAnalysis_WithNoSolarFigure()
    {
        var tool = BuildTool();
        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        var reference = BodyTextOfSection(_reportedContent!, "Sun and shadow");

        reference.Should().NotBeNull();
        reference.Should().Contain("Solar Analysis");
        reference.Should().Contain("site currently shown in the viewer");
        Regex.IsMatch(reference!, @"\d").Should().BeFalse("the reference must contain no solar figure");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldStateTheDimensionsItDoesNotAssess()
    {
        var tool = BuildTool();
        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        var statement = BodyTextOfSection(_reportedContent!, "Not assessed");

        statement.Should().NotBeNull();
        statement.Should().Contain("outside this specialist's current scope");
        statement.Should().NotContain("No data source available");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldEmitOnlyAllowedBlockKinds_AndProvenanceLast()
    {
        var tool = BuildTool();
        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        var blocks = Blocks(_reportedContent!);
        blocks.Select(block => block.GetProperty("kind").GetString())
            .Should().OnlyContain(kind => AllowedKinds.Contains(kind));

        blocks[^1].GetProperty("kind").GetString().Should().Be("keyValue");
        blocks[^1].GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("label").GetString())
            .Should().Contain("Analysis");
    }

    // ---------- the site point and the seams ----------

    [Fact]
    public async Task ExecuteAsync_ShouldQueryTheProvidersWithTheAnalysisPoint_NotTheInputNames()
    {
        var tool = BuildTool();
        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        await _flood.Received(1).ResolveAsync(Latitude, Longitude, Arg.Any<CancellationToken>());
        await _climate.Received(1).ResolveAsync(Latitude, Longitude, Arg.Any<CancellationToken>());
    }

    // ---------- failure ----------

    [Fact]
    public async Task ExecuteAsync_ShouldReportASpecialistFailure_WhenAProviderThrows()
    {
        var fault = new InvalidOperationException("provider blew up");
        _flood.ResolveAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DataResolutionOutcome<FloodData>>(fault));

        var tool = BuildTool();
        var result = await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be(fault.Message);

        await _relay.Received(1).ReportFailureAsync(
            _analysisId, SiteAnalysisType.EnvironmentalContext, fault.Message,
            Arg.Is<Exception?>(cause => cause == fault), Arg.Any<CancellationToken>());
        await _relay.DidNotReceive().ReportSuccessAsync(
            Arg.Any<Guid>(), Arg.Any<SiteAnalysisType>(), Arg.Any<SiteAnalysisResultMetadata>(),
            Arg.Any<JsonDocument>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportASpecialistFailure_WhenTheAnalysisCannotBeFound()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((SiteAnalysisAggregate?)null);

        var tool = BuildTool();
        var result = await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        await _relay.Received(1).ReportFailureAsync(
            _analysisId, SiteAnalysisType.EnvironmentalContext, Arg.Any<string>(), null, Arg.Any<CancellationToken>());
        await _relay.DidNotReceive().ReportSuccessAsync(
            Arg.Any<Guid>(), Arg.Any<SiteAnalysisType>(), Arg.Any<SiteAnalysisResultMetadata>(),
            Arg.Any<JsonDocument>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRethrowCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _flood.ResolveAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<DataResolutionOutcome<FloodData>>(cancellation.Token));

        var tool = BuildTool();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tool.ExecuteAsync(Context(), Input(), cancellation.Token));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailWithoutTouchingTheRelay_WhenTheIdIsInvalid()
    {
        var tool = BuildTool();

        var result = await tool.ExecuteAsync(Context(), JsonDocument.Parse("{}"), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        await _relay.DidNotReceive().ReportSuccessAsync(
            Arg.Any<Guid>(), Arg.Any<SiteAnalysisType>(), Arg.Any<SiteAnalysisResultMetadata>(),
            Arg.Any<JsonDocument>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _relay.DidNotReceive().ReportFailureAsync(
            Arg.Any<Guid>(), Arg.Any<SiteAnalysisType>(), Arg.Any<string>(), Arg.Any<Exception?>(),
            Arg.Any<CancellationToken>());
    }

    // ---------- helpers ----------

    private static List<JsonElement> Blocks(JsonDocument content) =>
        [.. content.RootElement.GetProperty("blocks").EnumerateArray()];

    private static Dictionary<string, string> KeyValues(JsonDocument content)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var block in Blocks(content))
        {
            if (block.GetProperty("kind").GetString() != "keyValue")
            {
                continue;
            }

            foreach (var item in block.GetProperty("items").EnumerateArray())
            {
                values[item.GetProperty("label").GetString()!] = item.GetProperty("value").GetString()!;
            }
        }

        return values;
    }

    private static string? BodyTextOfSection(JsonDocument content, string heading)
    {
        var blocks = Blocks(content);
        var headingIndex = blocks.FindIndex(block =>
            block.GetProperty("kind").GetString() == "heading" &&
            block.GetProperty("text").GetString() == heading);

        if (headingIndex < 0)
        {
            return null;
        }

        for (var index = headingIndex + 1; index < blocks.Count; index++)
        {
            var kind = blocks[index].GetProperty("kind").GetString();
            if (kind == "heading")
            {
                return null;
            }

            if (kind == "text")
            {
                return blocks[index].GetProperty("text").GetString();
            }
        }

        return null;
    }
}
