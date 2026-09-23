using AskLucy.Application.CustomModels.Abstractions;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using NSubstitute.ClearExtensions;

namespace AskLucy.Web.Tests.CustomModels;

/// <summary>
/// One real host per test class, with the repository, the deployment target, the job client and
/// the Hugging Face source replaced, so nothing writes to the shared database, queues a real job or
/// leaves the machine. Building a host per test instead (via <c>WithWebHostBuilder</c> in the
/// constructor) started a Hangfire server against the shared test database for every test, which
/// took minutes and stalled when the classes ran together. xunit runs a class's tests one at a
/// time, so each test calls <see cref="Reset"/> and stubs what it needs.
/// </summary>
public sealed class CustomModelsApiFactory : CustomWebApplicationFactory
{
    public ICustomModelRepository Repository { get; } = Substitute.For<ICustomModelRepository>();

    public IDeploymentTargetSettingsProvider DeploymentTarget { get; } = Substitute.For<IDeploymentTargetSettingsProvider>();

    public IBackgroundJobClient Jobs { get; } = Substitute.For<IBackgroundJobClient>();

    public IModelRepositorySource HuggingFace { get; } = Substitute.For<IModelRepositorySource>();

    /// <summary>Forgets every stub and received call left by the previous test.</summary>
    public void Reset()
    {
        Repository.ClearSubstitute(ClearOptions.All);
        DeploymentTarget.ClearSubstitute(ClearOptions.All);
        Jobs.ClearSubstitute(ClearOptions.All);
        HuggingFace.ClearSubstitute(ClearOptions.All);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICustomModelRepository>();
            services.AddSingleton(Repository);
            services.RemoveAll<IDeploymentTargetSettingsProvider>();
            services.AddSingleton(DeploymentTarget);
            services.RemoveAll<IBackgroundJobClient>();
            services.AddSingleton(Jobs);
            services.RemoveAll<IModelRepositorySource>();
            services.AddSingleton(HuggingFace);
        });
    }
}
