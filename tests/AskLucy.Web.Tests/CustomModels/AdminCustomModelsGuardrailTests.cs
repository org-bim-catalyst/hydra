using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Domain.CustomModels;
using AskLucy.Web.Contracts;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace AskLucy.Web.Tests.CustomModels;

/// <summary>
/// specs/072 T058 — quickstart §4 over the real host: every unsafe source or destination is refused
/// with a 400 naming the field, before a record is saved or a job queued (FR-006, FR-008, SC-002).
/// </summary>
public sealed class AdminCustomModelsGuardrailTests : IClassFixture<CustomModelsApiFactory>
{
    private const string Source = "https://huggingface.co/Supertone/supertonic-3";
    private const string Destination = "Models/supertonic-3";

    private readonly ICustomModelRepository _repository;
    private readonly IDeploymentTargetSettingsProvider _deploymentTarget;
    private readonly IBackgroundJobClient _jobs;
    private readonly IModelRepositorySource _huggingFace;
    private readonly HttpClient _client;

    public AdminCustomModelsGuardrailTests(CustomModelsApiFactory factory)
    {
        factory.Reset();
        _repository = factory.Repository;
        _deploymentTarget = factory.DeploymentTarget;
        _jobs = factory.Jobs;
        _huggingFace = factory.HuggingFace;

        _deploymentTarget.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new DeploymentTargetSettings("ftp.example.test", 21, "deployer", "not-a-real-password", "/site", AllowPlainFtp: false));
        _jobs.Create(Arg.Any<Job>(), Arg.Any<IState>()).Returns("job-1");

        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    /// <summary>Plain <c>http://huggingface.co</c> is deliberately absent: spec edge cases upgrade it to https.</summary>
    public static TheoryData<string> UnsafeSources => new()
    {
        "https://evil.example/Supertone/supertonic-3",
        "https://huggingface.co@evil.example/x/y",
        "https://huggingface.co/datasets/x/y",
        "https://huggingface.co.evil.example/Supertone/supertonic-3",
        "https://127.0.0.1/Supertone/supertonic-3",
    };

    public static TheoryData<string> UnsafeDestinations => new()
    {
        "../etc",
        "Models/../../x",
        "/Models/x",
        "Models\\x",
        "Models/%2e%2e/x",
        "Models/x/CON",
        "Models",
        "wwwroot/x",
        "",
    };

    [Theory]
    [MemberData(nameof(UnsafeSources))]
    public async Task Submit_UnsafeSource_Returns400OnSource_AndStartsNothing(string source)
    {
        var response = await SubmitAsAdminAsync(source, Destination);

        await ShouldBeRefusedOnAsync(response, "source");
    }

    [Theory]
    [MemberData(nameof(UnsafeDestinations))]
    public async Task Submit_UnsafeDestination_Returns400OnDestination_AndStartsNothing(string destination)
    {
        var response = await SubmitAsAdminAsync(Source, destination);

        await ShouldBeRefusedOnAsync(response, "destination");
    }

    [Fact]
    public async Task Submit_ViewOnlyCaller_Returns403_AndStartsNothing()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtFactory.Create("viewer-1", [], ["admin.custom-models.view"]));

        var response = await _client.PostAsync(
            "/api/v1/admin/custom-models",
            JsonContent.Create(new SubmitCustomModelRequest(Source, Destination, null)),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ShouldHaveStartedNothingAsync();
    }

    private Task<HttpResponseMessage> SubmitAsAdminAsync(string source, string destination)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("admin-1", "Administrator"));

        return _client.PostAsync(
            "/api/v1/admin/custom-models",
            JsonContent.Create(new SubmitCustomModelRequest(source, destination, null)),
            TestContext.Current.CancellationToken);
    }

    private async Task ShouldBeRefusedOnAsync(HttpResponseMessage response, string field)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("errors").EnumerateObject().Select(e => e.Name)
            .Should().Contain(name => string.Equals(name, field, StringComparison.OrdinalIgnoreCase));

        await ShouldHaveStartedNothingAsync();
    }

    private async Task ShouldHaveStartedNothingAsync()
    {
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        _jobs.DidNotReceiveWithAnyArgs().Create(default!, default!);
        _huggingFace.ReceivedCalls().Should().BeEmpty();
    }
}
