using System.Text.Json;
using System.Text.RegularExpressions;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Documents.Commands;
using AskLucy.Application.Documents.Processing;
using AskLucy.Application.MapCaptures;
using AskLucy.Application.MapCaptures.Queries.CaptureMap;
using AskLucy.Application.Options;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Application.SiteAnalysis.Tools;
using AskLucy.Domain.Common;
using AskLucy.Domain.Documents;
using AskLucy.Domain.SiteAnalysis;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using SiteAnalysisAggregate = AskLucy.Domain.SiteAnalysis.SiteAnalysis;

namespace AskLucy.Application.Tests.SiteAnalysis;

/// <summary>
/// specs/090-real-map-capture — the site-map specialist now shows the platform's real map capture.
/// Delivery, content shape, wording, provenance, confidence, capture parameters, document
/// persistence, every capture failure reason, and the additive identity pin. Mirrors
/// <see cref="EnvironmentalContextToolTests"/>'s substitution style; the document finalizer is the
/// real one over substituted ports (as in <c>UploadValidationTests</c>), so the persistence path is
/// genuinely exercised.
/// </summary>
public sealed class SiteSchematicImageGenerationToolTests
{
    private const string UserId = "user-1";
    private const string SiteName = "Al Barsha South";
    private const double Latitude = 25.09;
    private const double Longitude = 55.20;

    private readonly Guid _analysisId = Guid.CreateVersion7();

    private readonly ISiteAnalysisRepository _repository = Substitute.For<ISiteAnalysisRepository>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly ISiteAnalysisResultRelay _relay = Substitute.For<ISiteAnalysisResultRelay>();
    private readonly IDocumentFileValidator _fileValidator = Substitute.For<IDocumentFileValidator>();
    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
    private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
    private readonly IDocumentStatisticsRepository _statisticsRepository = Substitute.For<IDocumentStatisticsRepository>();
    private readonly IProcessingNotifier _processingNotifier = Substitute.For<IProcessingNotifier>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private CaptureMapQuery? _sentQuery;
    private SiteAnalysisResultMetadata? _reportedMetadata;
    private JsonDocument? _reportedContent;
    private Guid? _addedDocumentId;

    public SiteSchematicImageGenerationToolTests()
    {
        var analysis = SiteAnalysisAggregate.Create(UserId, Guid.NewGuid(), SiteName, Latitude, Longitude, null, 2, UserId);
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(analysis);

        _statisticsRepository.ComputeAggregateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentStatisticsAggregate(0, 0, null, "{}", "{}"));
        _fileValidator.ValidateAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(DocumentFileValidationResult.Valid(DocumentFileType.Png, "image/png"));
        _fileStorage.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("stored-site-map.png");
        _documentRepository.FindDocumentIdByChecksumAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);
        _documentRepository.When(repository => repository.Add(Arg.Any<Document>()))
            .Do(call => _addedDocumentId = call.Arg<Document>()!.Id);

        _relay
            .ReportSuccessAsync(
                Arg.Any<Guid>(), Arg.Any<SiteAnalysisType>(), Arg.Any<SiteAnalysisResultMetadata>(),
                Arg.Any<JsonDocument>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _reportedMetadata = call.Arg<SiteAnalysisResultMetadata>();
                _reportedContent = JsonDocument.Parse(call.Arg<JsonDocument>()!.RootElement.GetRawText());
                return Task.CompletedTask;
            });
    }

    private SiteSchematicImageGenerationTool BuildTool()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_repository);
        services.AddSingleton(_sender);
        services.AddSingleton(new DocumentUploadFinalizer(
            _fileValidator,
            _fileStorage,
            _documentRepository,
            _statisticsRepository,
            _processingNotifier,
            _unitOfWork,
            Microsoft.Extensions.Options.Options.Create(new DocumentUploadOptions()),
            Microsoft.Extensions.Options.Options.Create(new DocumentStorageQuotaOptions())));
        services.AddSingleton(_unitOfWork);

        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new SiteSchematicImageGenerationTool(scopeFactory, new SiteAnalysisContentComposer(), _relay);
    }

    private JsonDocument Input() =>
        JsonDocument.Parse(
            $$"""{"siteAnalysisId":"{{_analysisId}}","siteName":"Ignored name","siteLocation":"Ignored location"}""");

    private static AgentToolExecutionContext Context() =>
        new(Guid.NewGuid(), Guid.NewGuid(), UserId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private void CaptureSucceeds(MapCaptureSource source = MapCaptureSource.Esri)
    {
        _sender.Send<MapImageResult>(Arg.Any<CaptureMapQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _sentQuery = call.Arg<CaptureMapQuery>();
                return Task.FromResult(MapImageResult.Success([1, 2, 3], 1216, 1216) with
                {
                    ResolvedLocation = "25.09,55.2",
                    Source = source,
                });
            });
    }

    private void CaptureFails(MapCaptureFailureReason reason, string detail = "because")
    {
        _sender.Send<MapImageResult>(Arg.Any<CaptureMapQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _sentQuery = call.Arg<CaptureMapQuery>();
                return Task.FromResult(MapImageResult.Failure(reason, detail));
            });
    }

    // ---------- delivery, shape and provenance ----------

    [Fact]
    public async Task ExecuteAsync_ShouldDeliverTheCapture_AndPersistItAsADocument()
    {
        CaptureSucceeds();
        var tool = BuildTool();

        var result = await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("reported").GetBoolean().Should().BeTrue();

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _documentRepository.Received(1).Add(Arg.Is<Document>(document => document != null && document.OwnerId == UserId));
        await _relay.Received(1).ReportSuccessAsync(
            _analysisId, SiteAnalysisType.SchematicImage, Arg.Any<SiteAnalysisResultMetadata>(),
            Arg.Any<JsonDocument>(), Arg.Is<Guid?>(id => id == _addedDocumentId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldEmitHeadingTextOneImageAndProvenanceLast()
    {
        CaptureSucceeds();
        var tool = BuildTool();

        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        var blocks = Blocks(_reportedContent!);
        blocks.Select(block => block.GetProperty("kind").GetString())
            .Should().Equal("heading", "text", "image", "keyValue");

        blocks[0].GetProperty("text").GetString().Should().Be("Site Map");
        blocks[^1].GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("label").GetString())
            .Should().Contain("Analysis");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReferenceThePersistedDocumentInItsOnlyImageBlock()
    {
        CaptureSucceeds();
        var tool = BuildTool();

        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        var images = Blocks(_reportedContent!)
            .Where(block => block.GetProperty("kind").GetString() == "image")
            .ToList();

        images.Should().ContainSingle();
        images[0].GetProperty("fileId").GetString().Should().Be(_addedDocumentId!.Value.ToString());

        var alt = images[0].GetProperty("alt").GetString();
        alt.Should().NotBeNullOrWhiteSpace();
        alt!.Length.Should().BeInRange(1, 300);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReportTheSourceAndHighConfidenceInProvenance()
    {
        CaptureSucceeds(MapCaptureSource.Google);
        var tool = BuildTool();

        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        _reportedMetadata!.AnalysisType.Should().Be(SiteAnalysisType.SchematicImage);
        _reportedMetadata.SiteName.Should().Be(SiteName);
        _reportedMetadata.DataSource.Should().Be("map-capture:google");
        _reportedMetadata.ConfidenceLevel.Should().Be(SiteAnalysisConfidenceLevel.High);

        var provenance = KeyValues(_reportedContent!);
        provenance["Data source"].Should().Be("map-capture:google");
        provenance["Confidence"].Should().Be("High");
    }

    // ---------- the capture parameters ----------

    [Fact]
    public async Task ExecuteAsync_ShouldCaptureAtTheRecordsPoint_NotTheInputName()
    {
        CaptureSucceeds();
        var tool = BuildTool();

        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        _sentQuery.Should().NotBeNull();
        _sentQuery!.Location.Should().BeNull();
        _sentQuery.Latitude.Should().Be(Latitude);
        _sentQuery.Longitude.Should().Be(Longitude);
        _sentQuery.CallerName.Should().Be($"site-analysis:{_analysisId}");

        Blocks(_reportedContent!)[1].GetProperty("text").GetString()
            .Should().Contain(SiteName).And.NotContain("Ignored name");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNameOnlyTheMode_AndLeaveTheAdministratorDefaultsUnset()
    {
        CaptureSucceeds();
        var tool = BuildTool();

        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        _sentQuery!.Mode.Should().Be("satellite");
        _sentQuery.Overlays.Should().BeNull();
        _sentQuery.Source.Should().BeNull();
        _sentQuery.CropPixels.Should().BeNull();
        _sentQuery.Zoom.Should().BeNull();
        _sentQuery.ViewSizeMetres.Should().BeNull();
    }

    // ---------- honesty of the finding ----------

    [Fact]
    public async Task ExecuteAsync_ShouldStateTheImageryIsTheMostRecentAvailable_NotLiveAndUndated()
    {
        CaptureSucceeds();
        var tool = BuildTool();

        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        var body = Blocks(_reportedContent!)[1].GetProperty("text").GetString()!;

        body.Should().Contain("most recent available");
        body.Should().Contain("not a live view");
        body.Should().Contain("no capture date");
        Regex.IsMatch(body, @"\d").Should().BeFalse("the finding must not imply a capture date");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldMakeNoLivenessOrDateClaimInTheAltText()
    {
        CaptureSucceeds();
        var tool = BuildTool();

        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        var alt = Blocks(_reportedContent!)
            .Single(block => block.GetProperty("kind").GetString() == "image")
            .GetProperty("alt").GetString()!;

        alt.Should().Contain(SiteName);
        alt.Should().NotContainEquivalentOf("live");
        alt.Should().NotContainEquivalentOf("date");
        Regex.IsMatch(alt, @"\d").Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCarryNoExternalAddress()
    {
        CaptureSucceeds();
        var tool = BuildTool();

        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        var raw = _reportedContent!.RootElement.GetRawText();
        raw.Should().NotContain("http");
        raw.Should().NotContain("://");
        raw.Should().NotContain("/documents/");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldEmitNoAttributionLine_WhenTheConfiguredSourceRequiresNone()
    {
        CaptureSucceeds(MapCaptureSource.Esri);
        var tool = BuildTool();

        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        var raw = _reportedContent!.RootElement.GetRawText();
        raw.Should().NotContain("©");
        raw.Should().NotContainEquivalentOf("Source: Esri");
        raw.Should().NotContainEquivalentOf("Source: Google");

        // The source is still identifiable, in provenance.
        KeyValues(_reportedContent)["Data source"].Should().Be("map-capture:esri");
    }

    // ---------- capture failures ----------

    [Theory]
    [InlineData(MapCaptureFailureReason.LocationNotFound)]
    [InlineData(MapCaptureFailureReason.GeocodingUnavailable)]
    [InlineData(MapCaptureFailureReason.ModeUnavailable)]
    [InlineData(MapCaptureFailureReason.OverlayUnavailable)]
    [InlineData(MapCaptureFailureReason.ZoomUnavailable)]
    [InlineData(MapCaptureFailureReason.ProviderUnavailable)]
    [InlineData(MapCaptureFailureReason.CropTooSmall)]
    [InlineData(MapCaptureFailureReason.ImageTooLarge)]
    public async Task ExecuteAsync_ShouldRecordTheCaptureReason_AndDeliverNothing(MapCaptureFailureReason reason)
    {
        CaptureFails(reason, "the capture detail");
        var tool = BuildTool();

        var result = await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().StartWith(reason.ToString());

        await _relay.Received(1).ReportFailureAsync(
            _analysisId, SiteAnalysisType.SchematicImage,
            Arg.Is<string>(text => text != null && text.StartsWith($"{reason}:") && text.Contains("the capture detail")),
            null, Arg.Any<CancellationToken>());

        await _relay.DidNotReceive().ReportSuccessAsync(
            Arg.Any<Guid>(), Arg.Any<SiteAnalysisType>(), Arg.Any<SiteAnalysisResultMetadata>(),
            Arg.Any<JsonDocument>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _fileStorage.DidNotReceive().SaveAsync(
            Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _reportedContent.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRecordAFailure_WhenTheAnalysisCannotBeFound()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((SiteAnalysisAggregate?)null);
        var tool = BuildTool();

        var result = await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        await _relay.Received(1).ReportFailureAsync(
            _analysisId, SiteAnalysisType.SchematicImage, Arg.Any<string>(), null, Arg.Any<CancellationToken>());
        await _relay.DidNotReceive().ReportSuccessAsync(
            Arg.Any<Guid>(), Arg.Any<SiteAnalysisType>(), Arg.Any<SiteAnalysisResultMetadata>(),
            Arg.Any<JsonDocument>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRecordAFailure_WhenTheDocumentCannotBeStored()
    {
        CaptureSucceeds();
        _fileValidator.ValidateAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(DocumentFileValidationResult.Invalid("the bytes are not an image"));

        var tool = BuildTool();
        var result = await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        await _relay.Received(1).ReportFailureAsync(
            _analysisId, SiteAnalysisType.SchematicImage, "the bytes are not an image",
            Arg.Is<Exception?>(cause => cause is DomainRuleViolationException),
            Arg.Any<CancellationToken>());
        await _relay.DidNotReceive().ReportSuccessAsync(
            Arg.Any<Guid>(), Arg.Any<SiteAnalysisType>(), Arg.Any<SiteAnalysisResultMetadata>(),
            Arg.Any<JsonDocument>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailWithoutTouchingTheRelay_WhenTheIdIsInvalid()
    {
        var tool = BuildTool();

        var result = await tool.ExecuteAsync(
            Context(), JsonDocument.Parse("{}"), TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        await _relay.DidNotReceive().ReportSuccessAsync(
            Arg.Any<Guid>(), Arg.Any<SiteAnalysisType>(), Arg.Any<SiteAnalysisResultMetadata>(),
            Arg.Any<JsonDocument>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _relay.DidNotReceive().ReportFailureAsync(
            Arg.Any<Guid>(), Arg.Any<SiteAnalysisType>(), Arg.Any<string>(), Arg.Any<Exception?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRethrowCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _sender.Send<MapImageResult>(Arg.Any<CaptureMapQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<MapImageResult>(cancellation.Token));

        var tool = BuildTool();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tool.ExecuteAsync(Context(), Input(), cancellation.Token));

        await _relay.DidNotReceive().ReportFailureAsync(
            Arg.Any<Guid>(), Arg.Any<SiteAnalysisType>(), Arg.Any<string>(), Arg.Any<Exception?>(),
            Arg.Any<CancellationToken>());
    }

    // ---------- the document lifecycle ----------

    [Fact]
    public async Task ExecuteAsync_ShouldStoreTheCaptureAsAPngOwnedByTheAnalysedUser()
    {
        CaptureSucceeds();
        var tool = BuildTool();

        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        await _statisticsRepository.Received(1)
            .ComputeAggregateAsync(UserId, Arg.Any<CancellationToken>());
        await _fileStorage.Received(1).SaveAsync(
            Arg.Any<Stream>(),
            Arg.Is<string>(name => name != null && name.StartsWith("site-analysis-site-map-") && name.EndsWith(".png")),
            Arg.Any<CancellationToken>());
        _documentRepository.Received(1).Add(Arg.Is<Document>(document => document != null && document.OwnerId == UserId));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReuseAnExistingDocument_WhenTheBytesAlreadyExist()
    {
        CaptureSucceeds();
        var existingDocumentId = Guid.CreateVersion7();
        _documentRepository.FindDocumentIdByChecksumAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(existingDocumentId);

        var tool = BuildTool();
        await tool.ExecuteAsync(Context(), Input(), TestContext.Current.CancellationToken);

        Blocks(_reportedContent!)
            .Single(block => block.GetProperty("kind").GetString() == "image")
            .GetProperty("fileId").GetString()
            .Should().Be(existingDocumentId.ToString());

        _documentRepository.DidNotReceive().Add(Arg.Any<Document>());
    }

    // ---------- additive identity ----------

    [Fact]
    public void Tool_ShouldKeepItsStableIdentity()
    {
        var tool = BuildTool();

        tool.Name.Should().Be("site_analysis_schematic_image");
        tool.InputSchemaJson.Should().Contain("siteAnalysisId").And.Contain("siteName").And.Contain("siteLocation");
        tool.OutputSchemaJson.Should().Contain("reported");
        SiteAnalysisSpecialists.All.Select(specialist => specialist.ToolName).Should().Contain(tool.Name);
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
}
