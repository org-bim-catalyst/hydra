using AskLucy.Application.Agents.Tools;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Application.SiteAnalysis.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.SiteAnalysis;

public sealed class UrbanContextToolTests
{
    private const string UserId = "user-1";

    [Fact]
    public void UrbanContextTool_ShouldHaveCorrectName()
    {
        // Arrange
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var composer = Substitute.For<SiteAnalysisContentComposer>();
        var relay = Substitute.For<ISiteAnalysisResultRelay>();

        // Act
        var tool = new UrbanContextTool(scopeFactory, composer, relay);

        // Assert
        tool.Name.Should().Be("site_analysis_urban_context");
    }

    [Fact]
    public void UrbanContextTool_ShouldHaveCorrectDescription()
    {
        // Arrange
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var composer = Substitute.For<SiteAnalysisContentComposer>();
        var relay = Substitute.For<ISiteAnalysisResultRelay>();

        // Act
        var tool = new UrbanContextTool(scopeFactory, composer, relay);

        // Assert
        tool.Description.Should()
            .Contain("surrounding land use mix")
            .And.Contain("building density")
            .And.Contain("notable nearby places");
    }

    [Fact]
    public void UrbanContextTool_ShouldHaveLowRiskLevel()
    {
        // Arrange
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var composer = Substitute.For<SiteAnalysisContentComposer>();
        var relay = Substitute.For<ISiteAnalysisResultRelay>();

        // Act
        var tool = new UrbanContextTool(scopeFactory, composer, relay);

        // Assert
        tool.RiskLevel.Should().Be(AgentToolRiskLevel.Low);
    }

    [Fact]
    public void UrbanContextTool_ShouldRequireExternalNetworkPermission()
    {
        // Arrange
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var composer = Substitute.For<SiteAnalysisContentComposer>();
        var relay = Substitute.For<ISiteAnalysisResultRelay>();

        // Act
        var tool = new UrbanContextTool(scopeFactory, composer, relay);

        // Assert
        tool.RequiredPermissions.Should().Contain(AgentToolPermission.ExternalNetwork);
        tool.RequiredPermissions.Should().NotContain(AgentToolPermission.WriteFile);
    }

    [Fact]
    public void UrbanContextTool_InputSchema_ShouldRequireSiteAnalysisIdSiteNameAndSiteLocation()
    {
        // Arrange
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var composer = Substitute.For<SiteAnalysisContentComposer>();
        var relay = Substitute.For<ISiteAnalysisResultRelay>();

        // Act
        var tool = new UrbanContextTool(scopeFactory, composer, relay);

        // Assert
        tool.InputSchemaJson.Should().Contain("siteAnalysisId");
        tool.InputSchemaJson.Should().Contain("siteName");
        tool.InputSchemaJson.Should().Contain("siteLocation");
    }

    [Fact]
    public void UrbanContextTool_OutputSchema_ShouldRequireReported()
    {
        // Arrange
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var composer = Substitute.For<SiteAnalysisContentComposer>();
        var relay = Substitute.For<ISiteAnalysisResultRelay>();

        // Act
        var tool = new UrbanContextTool(scopeFactory, composer, relay);

        // Assert
        tool.OutputSchemaJson.Should().Contain("reported");
    }

    [Fact]
    public async Task UrbanContextTool_ExecuteAsync_ShouldReportFailure_WhenSiteAnalysisNotFound()
    {
        // Arrange
        var mockScopeFactory = Substitute.For<IServiceScopeFactory>();
        var mockComposer = Substitute.For<SiteAnalysisContentComposer>();
        var mockRelay = Substitute.For<ISiteAnalysisResultRelay>();

        using var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.GetRequiredService<ISiteAnalysisRepository>()
            .Returns(Substitute.For<ISiteAnalysisRepository>());
        mockScopeFactory.CreateScope().Returns(scope);
        mockComposer.ComposeFinding(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<(Guid, string)?>(), Arg.Any<SiteAnalysisResultMetadata>())
            .Returns(JsonDocument.Parse("{\"version\":1,\"blocks\":[]}"));

        var tool = new UrbanContextTool(mockScopeFactory, mockComposer, mockRelay);

        var input = JsonSerializer.SerializeToDocument(new { siteAnalysisId = Guid.NewGuid(), siteName = "Test Site", siteLocation = "25.09, 55.20" });

        // Act
        var result = await tool.ExecuteAsync(
            new AgentToolExecutionContext { ExecutionId = "exec-1", StepId = "step-1", UserId = "user-1", AgentId = "agent-1", AgentVersionId = "1" },
            input,
            CancellationToken.None);

        // Assert
        await mockRelay.Received(1).ReportFailureAsync(
            Arg.Any<Guid>(),
            Arg.Any<SiteAnalysisType>(),
            Arg.Any<string>(),
            Arg.Any<Exception>(),
            Arg.Any<CancellationToken>());
        result.Should().BeOfType<AgentToolResult.Failure>();
    }

    [Fact]
    public async Task UrbanContextTool_ExecuteAsync_ShouldReportSuccess_WhenSiteFound()
    {
        // Arrange
        var mockScopeFactory = Substitute.For<IServiceScopeFactory>();
        var mockComposer = Substitute.For<SiteAnalysisContentComposer>();
        var mockRelay = Substitute.For<ISiteAnalysisResultRelay>();

        using var scope = Substitute.For<IServiceScope>();
        var mockRepo = Substitute.For<ISiteAnalysisRepository>();
        mockRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNullForAnyArgs();
        scope.ServiceProvider.GetRequiredService<ISiteAnalysisRepository>().Returns(mockRepo);
        mockScopeFactory.CreateScope().Returns(scope);
        mockComposer.ComposeFinding(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<(Guid, string)?>(), Arg.Any<SiteAnalysisResultMetadata>())
            .Returns(JsonDocument.Parse("{\"version\":1,\"blocks\":[]}"));

        var tool = new UrbanContextTool(mockScopeFactory, mockComposer, mockRelay);

        var input = JsonSerializer.SerializeToDocument(new { siteAnalysisId = Guid.NewGuid(), siteName = "Test Site", siteLocation = "25.09, 55.20" });

        // Act
        var result = await tool.ExecuteAsync(
            new AgentToolExecutionContext { ExecutionId = "exec-1", StepId = "step-1", UserId = "user-1", AgentId = "agent-1", AgentVersionId = "1" },
            input,
            CancellationToken.None);

        // Assert
        await mockRelay.DidNotReceiveWithAnyArgs().ReportFailureAsync(Arg.Any<Guid>(), Arg.Any<SiteAnalysisType>(), Arg.Any<string>(), Arg.Any<Exception>(), Arg.Any<CancellationToken>());
        result.Should().BeOfType<AgentToolResult.Success>();
    }
}