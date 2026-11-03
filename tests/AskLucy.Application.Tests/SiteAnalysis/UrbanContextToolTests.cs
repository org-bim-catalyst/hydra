using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Overpass;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Application.SiteAnalysis.Tools;
using AskLucy.Domain.Common;
using AskLucy.Domain.SiteAnalysis;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using SiteAnalysisAggregate = AskLucy.Domain.SiteAnalysis.SiteAnalysis;

namespace AskLucy.Application.Tests.SiteAnalysis;

/// <summary>
/// The urban-context specialist's contract: name, description, risk, permissions, schemas, and the
/// two delivery paths (site not found, and a successful OSM read). Mirrors
/// <see cref="EnvironmentalContextToolTests"/>'s real-<see cref="ServiceCollection"/> substitution
/// style so <c>GetRequiredService</c> resolves the tool's scoped dependencies.
/// </summary>
public sealed class UrbanContextToolTests
{
    private const string UserId = "user-1";
    private const string SiteName = "Al Barsha South";
    private const double Latitude = 25.09;
    private const double Longitude = 55.20;

    private readonly Guid _analysisId = Guid.CreateVersion7();

    private readonly ISiteAnalysisRepository _repository = Substitute.For<ISiteAnalysisRepository>();
    private readonly IOverpassFeatureQueryProvider _overpass = Substitute.For<IOverpassFeatureQueryProvider>();
    private readonly ISiteAnalysisResultRelay _relay = Substitute.For<ISiteAnalysisResultRelay>();

    private JsonDocument? _reportedContent;

    public UrbanContextToolTests()
    {
        var analysis = SiteAnalysisAggregate.Create(
            UserId, Guid.NewGuid(), SiteName, Latitude, Longitude, null, 2, UserId);
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(analysis);

        _overpass.QueryAsync(Arg.Any<OverpassFeatureQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);

        _relay
            .ReportSuccessAsync(
                Arg.Any<Guid>(), Arg.Any<SiteAnalysisType>(), Arg.Any<SiteAnalysisResultMetadata>(),
                Arg.Any<JsonDocument>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _reportedContent = JsonDocument.Parse(call.Arg<JsonDocument>().RootElement.GetRawText());
                return Task.CompletedTask;
            });
    }

    private UrbanContextTool BuildTool()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_repository);
        services.AddSingleton(_overpass);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new UrbanContextTool(scopeFactory, new SiteAnalysisContentComposer(), _relay);
    }

    private JsonDocument Input() =>
        JsonDocument.Parse(
            $$"""{"siteAnalysisId":"{{_analysisId}}","siteName":"Ignored name","siteLocation":"Ignored location"}""");

    private static AgentToolExecutionContext Context() =>
        new(Guid.NewGuid(), Guid.NewGuid(), UserId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void ShouldHaveCorrectName()
    {
        BuildTool().Name.Should().Be("site_analysis_urban_context");
    }

    [Fact]
    public void ShouldDescribeItsScope()
    {
        BuildTool().Description.Should()
            .Contain("surrounding land use mix")
            .And.Contain("building density")
            .And.Contain("notable nearby places");
    }

    [Fact]
    public void ShouldBeLowRisk()
    {
        BuildTool().RiskLevel.Should().Be(AgentToolRiskLevel.Low);
    }

    [Fact]
    public void ShouldRequireExternalNetworkOnly()
    {
        var tool = BuildTool();

        tool.RequiredPermissions.Should().Contain(AgentToolPermission.ExternalNetwork);
        tool.RequiredPermissions.Should().NotContain(AgentToolPermission.WriteFile);
    }

    [Fact]
    public void InputSchemaShouldRequireSiteIdentity()
    {
        BuildTool().InputSchemaJson.Should()
            .Contain("siteAnalysisId")
            .And.Contain("siteName")
            .And.Contain("siteLocation");
    }

    [Fact]
    public void OutputSchemaShouldRequireReported()
    {
        BuildTool().OutputSchemaJson.Should().Contain("reported");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportFailure_WhenSiteAnalysisNotFound()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((SiteAnalysisAggregate?)null);

        var result = await BuildTool().ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        await _relay.Received(1).ReportFailureAsync(
            _analysisId,
            SiteAnalysisType.UrbanContext,
            Arg.Any<string>(),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportSuccess_WhenSiteFound()
    {
        _overpass.QueryAsync(Arg.Any<OverpassFeatureQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                new OverpassFeature(
                    "1", "way",
                    new Dictionary<string, string> { ["amenity"] = "cafe" },
                    [new GeoPoint(Latitude, Longitude)]),
                new OverpassFeature(
                    "2", "way",
                    new Dictionary<string, string> { ["building"] = "yes" },
                    [new GeoPoint(Latitude, Longitude)]),
            ]);

        var result = await BuildTool().ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("reported").GetBoolean().Should().BeTrue();

        await _relay.Received(1).ReportSuccessAsync(
            _analysisId,
            SiteAnalysisType.UrbanContext,
            Arg.Any<SiteAnalysisResultMetadata>(),
            Arg.Any<JsonDocument>(),
            Guid.Empty,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectInputWithoutSiteAnalysisId()
    {
        var input = JsonDocument.Parse("""{"siteName":"x","siteLocation":"y"}""");

        var result = await BuildTool().ExecuteAsync(Context(), input, TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        await _relay.DidNotReceive().ReportSuccessAsync(
            Arg.Any<Guid>(),
            Arg.Any<SiteAnalysisType>(),
            Arg.Any<SiteAnalysisResultMetadata>(),
            Arg.Any<JsonDocument>(),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }
}
