using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Common;
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
/// specs/072 T033 — the admin Custom Models API over the real host. The repository, the deployment
/// target, the job client and the Hugging Face source are replaced (<see cref="CustomModelsApiFactory"/>),
/// so nothing here writes to the shared database, queues a real job or leaves the machine.
/// </summary>
public sealed class AdminCustomModelsControllerTests : IClassFixture<CustomModelsApiFactory>
{
    private const string Source = "https://huggingface.co/Supertone/supertonic-3";
    private const string Host = "ftp.sentinel-host.test";
    private const string Username = "sentinel-user";
    private const string Password = "sentinel-password-4b7d";
    private const string RootPath = "/sentinel-root";

    private static readonly Guid SomeModelId = Guid.NewGuid();

    private readonly ICustomModelRepository _repository;
    private readonly IDeploymentTargetSettingsProvider _deploymentTarget;
    private readonly IBackgroundJobClient _jobs;
    private readonly IModelRepositorySource _huggingFace;
    private readonly HttpClient _client;

    public AdminCustomModelsControllerTests(CustomModelsApiFactory factory)
    {
        factory.Reset();
        _repository = factory.Repository;
        _deploymentTarget = factory.DeploymentTarget;
        _jobs = factory.Jobs;
        _huggingFace = factory.HuggingFace;

        _deploymentTarget.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new DeploymentTargetSettings(Host, 21, Username, Password, RootPath, AllowPlainFtp: false));
        _jobs.Create(Arg.Any<Job>(), Arg.Any<IState>()).Returns("job-1");
        _repository.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((Array.Empty<CustomModel>(), 0));

        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public static TheoryData<string, string> ViewEndpoints => new()
    {
        { "GET", "/api/v1/admin/custom-models" },
        { "GET", "/api/v1/admin/custom-models/{id}" },
        { "GET", "/api/v1/admin/custom-models/deployment-status" },
    };

    public static TheoryData<string, string> ManageEndpoints => new()
    {
        { "POST", "/api/v1/admin/custom-models/source-preview" },
        { "POST", "/api/v1/admin/custom-models" },
        { "POST", "/api/v1/admin/custom-models/{id}/actions/cancel" },
        { "PUT", "/api/v1/admin/custom-models/{id}/availability" },
        { "DELETE", "/api/v1/admin/custom-models/{id}" },
    };

    public static TheoryData<string, string> Endpoints
    {
        get
        {
            var all = new TheoryData<string, string>();
            foreach (var row in ViewEndpoints.Concat(ManageEndpoints))
            {
                all.Add(row.Data.Item1, row.Data.Item2);
            }

            return all;
        }
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ShouldReturn401_WhenAnonymous(string method, string pathTemplate)
    {
        var response = await SendAsync(method, pathTemplate);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ShouldReturn403_WhenCallerHasNoAdminRole(string method, string pathTemplate)
    {
        Authorize("user-1");

        var response = await SendAsync(method, pathTemplate);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ShouldPassTheGate_WithTheCustomModelPermissions(string method, string pathTemplate)
    {
        AuthorizeWith("admin.custom-models.view", "admin.custom-models.manage");

        var response = await SendAsync(method, pathTemplate);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized).And.NotBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(ViewEndpoints))]
    public async Task ViewPermission_ShouldPassTheGate_OnViewEndpoints(string method, string pathTemplate)
    {
        AuthorizeWith("admin.custom-models.view");

        var response = await SendAsync(method, pathTemplate);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized).And.NotBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(ManageEndpoints))]
    public async Task ViewPermission_ShouldReturn403_OnManageEndpoints(string method, string pathTemplate)
    {
        AuthorizeWith("admin.custom-models.view");

        var response = await SendAsync(method, pathTemplate);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>FR-026 — managing AI providers grants nothing here.</summary>
    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task AiProvidersManage_ShouldReturn403_Everywhere(string method, string pathTemplate)
    {
        AuthorizeWith("admin.ai-providers.view", "admin.ai-providers.manage");

        var response = await SendAsync(method, pathTemplate);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Submit_ShouldReturn202WithLocation_AndQueueTheJob()
    {
        Authorize("admin-1", "Administrator");

        var response = await _client.PostAsync(
            "/api/v1/admin/custom-models",
            JsonContent.Create(new SubmitCustomModelRequest(Source, "Models/supertonic-3", null)),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var id = body.RootElement.GetProperty("id").GetGuid();
        body.RootElement.GetProperty("deploymentState").GetString().Should().Be("Queued");
        body.RootElement.GetProperty("name").GetString().Should().Be("supertonic-3");
        response.Headers.Location!.ToString().Should().EndWith($"/api/v1/admin/custom-models/{id}");

        await _repository.Received(1).AddAsync(Arg.Is<CustomModel>(m => m!.Id == id), Arg.Any<CancellationToken>());
        _jobs.Received(1).Create(Arg.Is<Job>(j => j!.Method.Name == "RunAsync"), Arg.Any<IState>());
    }

    [Fact]
    public async Task Submit_ShouldReturn400DeploymentNotConfigured_WhenTheTargetIsMissing()
    {
        _deploymentTarget.GetAsync(Arg.Any<CancellationToken>()).Returns((DeploymentTargetSettings?)null);
        Authorize("admin-1", "Administrator");

        var response = await _client.PostAsync(
            "/api/v1/admin/custom-models",
            JsonContent.Create(new SubmitCustomModelRequest(Source, "Models/supertonic-3", null)),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("type").GetString().Should().Be("https://hydra.bimcatalyst.com/problems/deployment-not-configured");
        _jobs.DidNotReceiveWithAnyArgs().Create(default!, default!);
    }

    [Fact]
    public async Task List_ShouldReturn200WithPagedShape()
    {
        Authorize("admin-1", "Administrator");

        var response = await _client.GetAsync("/api/v1/admin/custom-models?page=1&pageSize=500", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
        body.RootElement.GetProperty("totalCount").GetInt32().Should().Be(0);
        body.RootElement.GetProperty("pageSize").GetInt32().Should().Be(100);
    }

    [Fact]
    public async Task Get_ShouldReturn404_ForAnUnknownId()
    {
        Authorize("admin-1", "Administrator");

        var response = await _client.GetAsync($"/api/v1/admin/custom-models/{SomeModelId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeploymentStatus_ShouldReturn200_WithoutAnyTargetSecret()
    {
        Authorize("admin-1", "Administrator");

        var response = await _client.GetAsync("/api/v1/admin/custom-models/deployment-status", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        text.Should().NotContain(Host).And.NotContain(Username).And.NotContain(Password).And.NotContain(RootPath);
        using var body = JsonDocument.Parse(text);
        body.RootElement.GetProperty("isConfigured").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("transport").GetString().Should().Be("FTPS");
        body.RootElement.GetProperty("allowedDestinationPrefixes").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(CustomModelsOptions.DefaultAllowedDestinationPrefixes);
    }

    [Fact]
    public async Task SourcePreview_ShouldDeriveTheName_WithoutCallingHuggingFace()
    {
        _repository.NameExistsAsync("supertonic-3", Arg.Any<CancellationToken>()).Returns(true);
        Authorize("admin-1", "Administrator");

        var response = await _client.PostAsync(
            "/api/v1/admin/custom-models/source-preview",
            JsonContent.Create(new PreviewCustomModelSourceRequest($"{Source}/tree/v2")),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("isValid").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("repositoryId").GetString().Should().Be("Supertone/supertonic-3");
        body.RootElement.GetProperty("revision").GetString().Should().Be("v2");
        body.RootElement.GetProperty("derivedName").GetString().Should().Be("supertonic-3");
        body.RootElement.GetProperty("nameAvailable").GetBoolean().Should().BeFalse();
        _huggingFace.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Cancel_ShouldReturn202_AndCancelAQueuedDeployment()
    {
        var model = QueuedModel();
        ApplyUpdatesTo(model);
        Authorize("admin-1", "Administrator");

        var response = await _client.PostAsync($"/api/v1/admin/custom-models/{model.Id}/actions/cancel", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("deploymentState").GetString().Should().Be("Cancelled");
        model.CancelledByUserId.Should().Be("admin-1");
    }

    [Fact]
    public async Task Cancel_ShouldReturn409_WhenTheDeploymentAlreadyFinished()
    {
        var model = QueuedModel();
        model.StartListing(DateTime.UtcNow);
        model.Fail(CustomModelFailureKind.SourceNotFound, "Not found.", DateTime.UtcNow);
        ApplyUpdatesTo(model);
        Authorize("admin-1", "Administrator");

        var response = await _client.PostAsync($"/api/v1/admin/custom-models/{model.Id}/actions/cancel", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("type").GetString().Should().Be("https://hydra.bimcatalyst.com/problems/custom-model-not-in-progress");
    }

    [Fact]
    public async Task Cancel_ShouldReturn404_ForAnUnknownId()
    {
        Authorize("admin-1", "Administrator");

        var response = await _client.PostAsync($"/api/v1/admin/custom-models/{SomeModelId}/actions/cancel", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetAvailability_ShouldReturn200_AndMakeACompletedModelAvailable()
    {
        var model = CompletedModel();
        ApplyUpdatesTo(model);
        Authorize("admin-1", "Administrator");

        var response = await PutAvailabilityAsync(model.Id, "Available");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("availability").GetString().Should().Be("Available");
        model.Availability.Should().Be(CustomModelAvailability.Available);
    }

    [Fact]
    public async Task SetAvailability_ShouldReturn400_WhenTheDeploymentHasNotCompleted()
    {
        var model = QueuedModel();
        ApplyUpdatesTo(model);
        Authorize("admin-1", "Administrator");

        var response = await PutAvailabilityAsync(model.Id, "Available");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("detail").GetString().Should().Be("The deployment has not completed.");
    }

    [Fact]
    public async Task SetAvailability_ShouldReturn409NamingTheHolder_WhenAnotherModelIsAvailable()
    {
        var holder = CompletedModel("supertonic-3-v1");
        holder.MakeAvailable();
        var model = CompletedModel("supertonic-3-v2");
        ApplyUpdatesTo(model);
        _repository.FindAvailableForRepositoryAsync("Supertone/supertonic-3", Arg.Any<CancellationToken>()).Returns(holder);
        Authorize("admin-1", "Administrator");

        var response = await PutAvailabilityAsync(model.Id, "Available");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.RootElement.GetProperty("detail").GetString().Should().Contain("supertonic-3-v1");
    }

    /// <summary>
    /// Both requests pass the handler's check before either saves; the filtered unique index rejects
    /// the second save, which the real repository reports as <see cref="DuplicateResourceException"/>.
    /// </summary>
    [Fact]
    public async Task SetAvailability_ConcurrentRequestsForOneRepository_ShouldEndIn200And409()
    {
        var first = CompletedModel("supertonic-3-v1");
        var second = CompletedModel("supertonic-3-v2");
        var saves = 0;
        foreach (var model in new[] { first, second })
        {
            _repository.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);
            _repository.UpdateAsync(model.Id, Arg.Any<Func<CustomModel, bool>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    if (Interlocked.Increment(ref saves) > 1)
                    {
                        throw new DuplicateResourceException("Another model from this repository is already available. Make it unavailable first.");
                    }

                    call.Arg<Func<CustomModel, bool>>()!(model);
                    return model;
                });
        }

        Authorize("admin-1", "Administrator");

        var responses = await Task.WhenAll(PutAvailabilityAsync(first.Id, "Available"), PutAvailabilityAsync(second.Id, "Available"));

        responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
    }

    [Fact]
    public async Task SetAvailability_ShouldReturn400_ForAnUnknownValue()
    {
        Authorize("admin-1", "Administrator");

        var response = await PutAvailabilityAsync(SomeModelId, "Sometimes");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Remove_ShouldReturn204_AndSoftDeleteAFailedModel()
    {
        var model = QueuedModel();
        model.StartListing(DateTime.UtcNow);
        model.Fail(CustomModelFailureKind.SourceNotFound, "Not found.", DateTime.UtcNow);
        ApplyUpdatesTo(model);
        Authorize("admin-1", "Administrator");

        var response = await _client.DeleteAsync($"/api/v1/admin/custom-models/{model.Id}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        model.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Remove_ShouldReturn400_ForACompletedModel()
    {
        var model = CompletedModel();
        ApplyUpdatesTo(model);
        Authorize("admin-1", "Administrator");

        var response = await _client.DeleteAsync($"/api/v1/admin/custom-models/{model.Id}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        model.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Remove_ShouldReturn404_ForAnUnknownId()
    {
        Authorize("admin-1", "Administrator");

        var response = await _client.DeleteAsync($"/api/v1/admin/custom-models/{SomeModelId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static CustomModel CompletedModel(string name = "supertonic-3")
    {
        HuggingFaceModelSource.TryParse(Source, out var source, out _).Should().BeTrue();
        DeploymentDestination.TryCreate($"Models/{name}", CustomModelsOptions.DefaultAllowedDestinationPrefixes, out var destination, out _).Should().BeTrue();
        var model = CustomModel.Create(name, source!, destination!, "admin-1");
        model.StartListing(DateTime.UtcNow);
        model.BeginTransfer(new string('a', 40), 10, 1, 100);
        model.RecordProgress(10, 1, null, null, null);
        model.Complete(DateTime.UtcNow);
        return model;
    }

    private Task<HttpResponseMessage> PutAvailabilityAsync(Guid id, string availability) =>
        _client.PutAsync(
            $"/api/v1/admin/custom-models/{id}/availability",
            JsonContent.Create(new { availability }),
            TestContext.Current.CancellationToken);

    private static CustomModel QueuedModel()
    {
        HuggingFaceModelSource.TryParse(Source, out var source, out _).Should().BeTrue();
        DeploymentDestination.TryCreate("Models/supertonic-3", CustomModelsOptions.DefaultAllowedDestinationPrefixes, out var destination, out _).Should().BeTrue();
        return CustomModel.Create("supertonic-3", source!, destination!, "admin-1");
    }

    /// <summary>Makes the substitute repository behave like the real one for this record: load it, apply, save when accepted.</summary>
    private void ApplyUpdatesTo(CustomModel model)
    {
        _repository.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);
        _repository.UpdateAsync(model.Id, Arg.Any<Func<CustomModel, bool>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CustomModel, bool>>()!(model) ? model : null);
    }

    private void Authorize(string userId, params string[] roles) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create(userId, roles));

    /// <summary>A synthetic custom-role principal holding only <paramref name="permissions"/>.</summary>
    private void AuthorizeWith(params string[] permissions) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("custom-role-user", [], permissions));

    private Task<HttpResponseMessage> SendAsync(string method, string pathTemplate)
    {
        var path = pathTemplate.Replace("{id}", SomeModelId.ToString());

        return method switch
        {
            "GET" => _client.GetAsync(path),
            "PUT" => _client.PutAsync(path, JsonContent.Create(new { availability = "Unavailable" })),
            "DELETE" => _client.DeleteAsync(path),
            "POST" when path.EndsWith("/actions/cancel", StringComparison.Ordinal) => _client.PostAsync(path, null),
            "POST" when path.EndsWith("/source-preview", StringComparison.Ordinal) => _client.PostAsync(path, JsonContent.Create(new PreviewCustomModelSourceRequest(Source))),
            "POST" => _client.PostAsync(path, JsonContent.Create(new SubmitCustomModelRequest(Source, "Models/supertonic-3", null))),
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };
    }
}
