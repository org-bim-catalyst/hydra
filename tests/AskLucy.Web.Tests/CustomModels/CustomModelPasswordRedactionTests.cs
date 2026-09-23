using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.CustomModels.Jobs;
using AskLucy.Domain.CustomModels;
using AskLucy.Persistence;
using AskLucy.Web.Contracts;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using Xunit;

namespace AskLucy.Web.Tests.CustomModels;

/// <summary>
/// specs/072 T066 (SC-006) — the deployment target's secrets never leave the server. The target is
/// configured through the real <c>Ftp</c> section with sentinel values, and every endpoint, every
/// persisted column and every log line is checked for them.
/// </summary>
/// <remarks>
/// The persisted check reads each column value through the EF model of the entity about to be
/// saved, rather than from the database: this suite must not write to the shared database it
/// boots against (see <see cref="CustomWebApplicationFactory"/>). Nothing is persisted that the EF
/// model doesn't map, so every column is covered.
/// </remarks>
public sealed class CustomModelPasswordRedactionTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Source = "https://huggingface.co/Supertone/supertonic-3";

    // .invalid never resolves (RFC 2606), so the real uploader fails without leaving the machine.
    private const string Host = "sentinel-host.invalid";
    private const string Username = "sentinel-user-5c1e";
    private const string Password = "sentinel-password-4b7d";
    private const string RootPath = "/sentinel-root-9a2f";

    private static readonly string[] Secrets = [Host, Username, Password, RootPath];

    private readonly CustomWebApplicationFactory _factory;
    private readonly Dictionary<Guid, CustomModel> _models = [];
    private readonly List<CustomModelOverwrittenFile> _overwrittenFiles = [];
    private readonly ICustomModelRepository _repository = Substitute.For<ICustomModelRepository>();
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();
    private readonly IModelRepositorySource _huggingFace = Substitute.For<IModelRepositorySource>();
    private readonly FakeLogCollector _logs = new();
    private WebApplicationFactory<Program> _host = null!;

    public CustomModelPasswordRedactionTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;

        _repository.AddAsync(Arg.Any<CustomModel>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var model = call.Arg<CustomModel>()!;
                _models[model.Id] = model;
                return Task.CompletedTask;
            });
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => _models.GetValueOrDefault(call.Arg<Guid>()));
        _repository.UpdateAsync(Arg.Any<Guid>(), Arg.Any<Func<CustomModel, bool>>(), Arg.Any<CancellationToken>())
            .Returns(call => _models.TryGetValue(call.Arg<Guid>(), out var model) && call.Arg<Func<CustomModel, bool>>()!(model) ? model : null);
        _repository.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => ((IReadOnlyList<CustomModel>)_models.Values.ToList(), _models.Count));
        _repository.GetOverwrittenFilesAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var files = _overwrittenFiles.Where(f => f.CustomModelId == call.Arg<Guid>()).ToList();
                return ((IReadOnlyList<CustomModelOverwrittenFile>)files, files.Count);
            });
        _repository.AddOverwrittenFileAsync(Arg.Any<CustomModelOverwrittenFile>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _overwrittenFiles.Add(call.Arg<CustomModelOverwrittenFile>()!);
                return Task.CompletedTask;
            });
        _repository.FindCompletedForRepositoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomModel>());
        _repository.ListInProgressAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<CustomModel>());
        _repository.GetRunSignalAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(CustomModelRunSignal.Continue);

        _jobs.Create(Arg.Any<Job>(), Arg.Any<IState>()).Returns("job-1");

        _huggingFace.ResolveRevisionAsync("Supertone/supertonic-3", "main", Arg.Any<CancellationToken>())
            .Returns(new ResolvedRevision("Supertone/supertonic-3", new string('a', 40), IsPrivate: false, IsGated: false));
        _huggingFace.ListFilesAsync("Supertone/supertonic-3", new string('a', 40), Arg.Any<CancellationToken>())
            .Returns([new ModelRepositoryFile("config.json", 3, null, new string('b', 40))]);
        _huggingFace.DownloadAsync(default!, default!, default!, default!, default!, default)
            .ReturnsForAnyArgs(call => call.ArgAt<Stream>(3).WriteAsync("{ }"u8.ToArray()).AsTask());
    }

    [Fact]
    public async Task EveryEndpoint_ShouldNeverReturnTheTargetSecrets()
    {
        var client = CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var submitted = await client.PostAsync("/api/v1/admin/custom-models", JsonContent.Create(new SubmitCustomModelRequest(Source, "Models/supertonic-3", null)), ct);
        submitted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var id = _models.Keys.Single();

        var responses = new[]
        {
            submitted,
            await client.GetAsync("/api/v1/admin/custom-models", ct),
            await client.GetAsync($"/api/v1/admin/custom-models/{id}", ct),
            await client.GetAsync("/api/v1/admin/custom-models/deployment-status", ct),
            await client.PostAsync("/api/v1/admin/custom-models/source-preview", JsonContent.Create(new PreviewCustomModelSourceRequest(Source)), ct),
            await client.PostAsync("/api/v1/admin/custom-models", JsonContent.Create(new SubmitCustomModelRequest("https://example.com/not-hugging-face", "Models/other", null)), ct),
            await client.PostAsync($"/api/v1/admin/custom-models/{id}/actions/cancel", null, ct),
        };

        responses[5].StatusCode.Should().Be(HttpStatusCode.BadRequest);
        foreach (var response in responses)
        {
            await AssertNoSecretAsync(response);
        }

        AssertNoSecretPersisted();
        AssertNoSecretLogged();
    }

    [Fact]
    public async Task FailedDeployment_ShouldPersistAndLogNoSecret()
    {
        // The real configuration-backed provider and the real FluentFTP uploader: the connection
        // fails, and its message is what the admin sees as the failure reason.
        var client = CreateClient();
        var id = await SubmitAsync(client);

        var run = () => RunJobAsync(id);

        await run.Should().ThrowAsync<DeploymentTargetException>();
        _models[id].DeploymentState.Should().Be(CustomModelDeploymentState.Failed);
        await AssertNoSecretAsync(await client.GetAsync($"/api/v1/admin/custom-models/{id}", TestContext.Current.CancellationToken));
        AssertNoSecretPersisted();
        AssertNoSecretLogged();
    }

    [Fact]
    public async Task OverwritingDeployment_ShouldPersistNoSecret()
    {
        var session = Substitute.For<IDeploymentUploadSession>();
        session.GetRemoteFileSizeAsync("Models/supertonic-3/config.json", Arg.Any<CancellationToken>()).Returns(5L, 3L);
        var uploader = Substitute.For<IDeploymentFileUploader>();
        uploader.OpenSessionAsync(Arg.Any<DeploymentTargetSettings>(), Arg.Any<CancellationToken>()).Returns(session);
        var client = CreateClient(services =>
        {
            services.RemoveAll<IDeploymentFileUploader>();
            services.AddSingleton(uploader);
        });
        var id = await SubmitAsync(client);

        await RunJobAsync(id);

        _models[id].DeploymentState.Should().Be(CustomModelDeploymentState.Completed);
        _overwrittenFiles.Should().ContainSingle();
        await uploader.Received(1).OpenSessionAsync(
            Arg.Is<DeploymentTargetSettings>(s => s!.Host == Host && s.Password == Password && s.RootPath == RootPath),
            Arg.Any<CancellationToken>());
        await AssertNoSecretAsync(await client.GetAsync($"/api/v1/admin/custom-models/{id}", TestContext.Current.CancellationToken));
        AssertNoSecretPersisted();
        AssertNoSecretLogged();
    }

    private HttpClient CreateClient(Action<IServiceCollection>? configure = null)
    {
        _host = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ftp:Host"] = Host,
                ["Ftp:Port"] = "990",
                ["Ftp:Username"] = Username,
                ["Ftp:Password"] = Password,
                ["Ftp:RootPath"] = RootPath,
            }));
            builder.ConfigureLogging(logging => logging.AddProvider(new FakeLoggerProvider(_logs)));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICustomModelRepository>();
                services.AddSingleton(_repository);
                services.RemoveAll<IBackgroundJobClient>();
                services.AddSingleton(_jobs);
                services.RemoveAll<IModelRepositorySource>();
                services.AddSingleton(_huggingFace);
                configure?.Invoke(services);
            });
        });

        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("admin-1", "Administrator"));
        return client;
    }

    private static async Task<Guid> SubmitAsync(HttpClient client)
    {
        var response = await client.PostAsync(
            "/api/v1/admin/custom-models",
            JsonContent.Create(new SubmitCustomModelRequest(Source, "Models/supertonic-3", null)),
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private async Task RunJobAsync(Guid id)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICustomModelDeploymentJob>().RunAsync(id, TestContext.Current.CancellationToken);
    }

    private static async Task AssertNoSecretAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var headers = string.Join('\n', response.Headers.Concat(response.Content.Headers).SelectMany(h => h.Value.Select(v => $"{h.Key}: {v}")));
        foreach (var secret in Secrets)
        {
            body.Should().NotContain(secret, "{0} {1} must not return the deployment target's settings", response.RequestMessage?.Method, response.RequestMessage?.RequestUri);
            headers.Should().NotContain(secret);
        }
    }

    /// <summary>Every mapped column of every row the job and the handlers would save.</summary>
    private void AssertNoSecretPersisted()
    {
        using var scope = _host.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>().Model;
        var rows = _models.Values.Cast<object>().Concat(_overwrittenFiles).ToList();
        rows.Should().NotBeEmpty();

        foreach (var row in rows)
        {
            var entityType = model.FindEntityType(row.GetType())!;
            foreach (var property in entityType.GetProperties().Where(p => !p.IsShadowProperty()))
            {
                var value = Convert.ToString(property.GetGetter().GetClrValue(row), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                foreach (var secret in Secrets)
                {
                    value.Should().NotContain(secret, "{0}.{1} is persisted", entityType.ClrType.Name, property.Name);
                }
            }
        }
    }

    private void AssertNoSecretLogged()
    {
        _logs.GetSnapshot().Should().NotContain(
            r => r.Message.Contains(Password, StringComparison.Ordinal)
                || (r.Exception != null && r.Exception.ToString().Contains(Password, StringComparison.Ordinal))
                || (r.StructuredState != null && r.StructuredState.Any(kv => kv.Value != null && kv.Value.Contains(Password, StringComparison.Ordinal))));
    }
}
