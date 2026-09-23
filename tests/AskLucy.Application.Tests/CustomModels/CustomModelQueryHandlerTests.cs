using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.CustomModels;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.CustomModels.Queries.GetCustomModel;
using AskLucy.Application.CustomModels.Queries.GetDeploymentStatus;
using AskLucy.Application.CustomModels.Queries.ListCustomModels;
using AskLucy.Application.CustomModels.Queries.PreviewCustomModelSource;
using AskLucy.Application.Options;
using AskLucy.Domain.CustomModels;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AskLucy.Application.Tests.CustomModels;

/// <summary>specs/072 T040. The read side of the admin Custom Models API.</summary>
public sealed class CustomModelQueryHandlerTests
{
    private const string Source = "https://huggingface.co/Supertone/supertonic-3";
    private const string Host = "ftp.sentinel-host.test";
    private const string Username = "sentinel-user";
    private const string Password = "sentinel-password-9c1e";
    private const string RootPath = "/sentinel-root";

    private readonly FakeCustomModelRepository _repository = new();
    private readonly IDeploymentTargetSettingsProvider _deploymentTarget = Substitute.For<IDeploymentTargetSettingsProvider>();

    [Fact]
    public async Task List_ReturnsSummariesWithPaging()
    {
        await SeedAsync("first", "Models/first");
        await SeedAsync("second", "Models/second");
        await SeedAsync("third", "Models/third");

        var result = await CreateListHandler().Handle(new ListCustomModelsQuery(Page: 2, PageSize: 2), TestContext.Current.CancellationToken);

        result.TotalCount.Should().Be(3);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(2);
        result.Items.Should().ContainSingle();
        result.Items[0].SubmittedBy.DisplayName.Should().Be(CustomModelSummaryBuilder.UnknownUserDisplayName);
    }

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-3, 500, 1, ListCustomModelsQuery.MaxPageSize)]
    [InlineData(4, 25, 4, 25)]
    public async Task List_ClampsPaging(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        var result = await CreateListHandler().Handle(new ListCustomModelsQuery(page, pageSize), TestContext.Current.CancellationToken);

        result.Page.Should().Be(expectedPage);
        result.PageSize.Should().Be(expectedPageSize);
    }

    [Theory]
    [InlineData(false, "FTPS")]
    [InlineData(true, "FTP")]
    public async Task Status_Configured_ReportsTransportCapAndPrefixes(bool allowPlainFtp, string expectedTransport)
    {
        _deploymentTarget.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new DeploymentTargetSettings(Host, 21, Username, Password, RootPath, allowPlainFtp));

        var status = await CreateStatusHandler(new CustomModelsOptions { MaxDeploymentBytes = 1234, AllowedDestinationPrefixes = ["Models", "models", "Other"] })
            .Handle(new GetDeploymentStatusQuery(), TestContext.Current.CancellationToken);

        status.IsConfigured.Should().BeTrue();
        status.Transport.Should().Be(expectedTransport);
        status.MaxDeploymentBytes.Should().Be(1234);
        status.AllowedDestinationPrefixes.Should().Equal("Models", "Other");
    }

    [Fact]
    public async Task Status_NotConfigured_HasNoTransport()
    {
        _deploymentTarget.GetAsync(Arg.Any<CancellationToken>()).Returns((DeploymentTargetSettings?)null);

        var status = await CreateStatusHandler(new CustomModelsOptions()).Handle(new GetDeploymentStatusQuery(), TestContext.Current.CancellationToken);

        status.IsConfigured.Should().BeFalse();
        status.Transport.Should().BeNull();
        status.AllowedDestinationPrefixes.Should().Equal(CustomModelsOptions.DefaultAllowedDestinationPrefixes);
    }

    [Fact]
    public async Task Status_NeverCarriesTargetSecrets()
    {
        _deploymentTarget.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new DeploymentTargetSettings(Host, 21, Username, Password, RootPath, AllowPlainFtp: false));

        var status = await CreateStatusHandler(new CustomModelsOptions()).Handle(new GetDeploymentStatusQuery(), TestContext.Current.CancellationToken);

        var json = JsonSerializer.Serialize(status);
        json.Should().NotContain(Host).And.NotContain(Username).And.NotContain(Password).And.NotContain(RootPath);
    }

    [Fact]
    public async Task Preview_ValidSource_DerivesNameAndReportsItFree()
    {
        var preview = await CreatePreviewHandler().Handle(
            new PreviewCustomModelSourceQuery("https://huggingface.co/Supertone/supertonic-3/resolve/v1.2/onnx/model.onnx"),
            TestContext.Current.CancellationToken);

        preview.IsValid.Should().BeTrue();
        preview.Error.Should().BeNull();
        preview.RepositoryId.Should().Be("Supertone/supertonic-3");
        preview.Revision.Should().Be("v1.2");
        preview.IgnoredFilePath.Should().Be("onnx/model.onnx");
        preview.DerivedName.Should().Be("supertonic-3");
        preview.NameAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Preview_DerivedNameTaken_ReportsItUnavailable()
    {
        await SeedAsync("SUPERTONIC-3", "Models/existing");

        var preview = await CreatePreviewHandler().Handle(new PreviewCustomModelSourceQuery(Source), TestContext.Current.CancellationToken);

        preview.IsValid.Should().BeTrue();
        preview.NameAvailable.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example/Supertone/supertonic-3")]
    [InlineData("https://huggingface.co.evil.example/Supertone/supertonic-3")]
    public async Task Preview_InvalidSource_ReturnsTheParseErrorAndNothingElse(string? source)
    {
        var preview = await CreatePreviewHandler().Handle(new PreviewCustomModelSourceQuery(source), TestContext.Current.CancellationToken);

        preview.IsValid.Should().BeFalse();
        preview.Error.Should().NotBeNullOrWhiteSpace();
        preview.RepositoryId.Should().BeNull();
        preview.Revision.Should().BeNull();
        preview.DerivedName.Should().BeNull();
        preview.NameAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Get_ReturnsSummaryAndPagedOverwriteReport()
    {
        var model = await SeedAsync("tts", "Models/tts");
        model.StartListing(DateTime.UtcNow);
        model.BeginTransfer(new string('a', 40), totalBytes: 30, fileCount: 3, maxDeploymentBytes: 1000);
        foreach (var path in new[] { "a.onnx", "b.onnx", "c.onnx" })
        {
            await _repository.AddOverwrittenFileAsync(model.RecordOverwrite(path, 10, DateTime.UtcNow), TestContext.Current.CancellationToken);
        }

        var detail = await CreateGetHandler().Handle(new GetCustomModelQuery(model.Id, OverwrittenPage: 2, OverwrittenPageSize: 2), TestContext.Current.CancellationToken);

        detail.Id.Should().Be(model.Id);
        detail.Name.Should().Be("tts");
        detail.OverwrittenFiles.TotalCount.Should().Be(3);
        detail.OverwrittenFiles.Page.Should().Be(2);
        detail.OverwrittenFiles.Items.Select(f => f.RelativePath).Should().Equal("c.onnx");
    }

    [Fact]
    public async Task Get_ClampsOverwrittenPageSize()
    {
        var model = await SeedAsync("tts", "Models/tts");

        var detail = await CreateGetHandler().Handle(new GetCustomModelQuery(model.Id, 0, 10_000), TestContext.Current.CancellationToken);

        detail.OverwrittenFiles.Page.Should().Be(1);
        detail.OverwrittenFiles.PageSize.Should().Be(GetCustomModelQuery.MaxOverwrittenPageSize);
    }

    [Fact]
    public async Task Get_UnknownOrRemoved_ThrowsNotFound()
    {
        var removed = await SeedAsync("gone", "Models/gone");
        removed.Fail(CustomModelFailureKind.Unexpected, "failed", DateTime.UtcNow);
        removed.Remove("admin", DateTime.UtcNow);

        foreach (var id in new[] { Guid.NewGuid(), removed.Id })
        {
            var act = () => CreateGetHandler().Handle(new GetCustomModelQuery(id), TestContext.Current.CancellationToken);
            await act.Should().ThrowAsync<KeyNotFoundException>();
        }
    }

    private async Task<CustomModel> SeedAsync(string name, string destination)
    {
        HuggingFaceModelSource.TryParse(Source, out var source, out _).Should().BeTrue();
        DeploymentDestination.TryCreate(destination, CustomModelsOptions.DefaultAllowedDestinationPrefixes, out var target, out _).Should().BeTrue();
        var model = CustomModel.Create(name, source!, target!, "someone");
        await _repository.AddAsync(model, TestContext.Current.CancellationToken);
        return model;
    }

    private ListCustomModelsQueryHandler CreateListHandler() =>
        new(_repository, new CustomModelSummaryBuilder(Substitute.For<IUserAdminRepository>(), []));

    private GetDeploymentStatusQueryHandler CreateStatusHandler(CustomModelsOptions value)
    {
        var options = Substitute.For<IOptionsMonitor<CustomModelsOptions>>();
        options.CurrentValue.Returns(value);
        return new GetDeploymentStatusQueryHandler(_deploymentTarget, options);
    }

    private GetCustomModelQueryHandler CreateGetHandler() =>
        new(_repository, new CustomModelSummaryBuilder(Substitute.For<IUserAdminRepository>(), []));

    private PreviewCustomModelSourceQueryHandler CreatePreviewHandler() => new(_repository);
}
