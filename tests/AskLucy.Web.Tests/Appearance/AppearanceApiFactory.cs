using AskLucy.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using NSubstitute.ClearExtensions;

namespace AskLucy.Web.Tests.Appearance;

/// <summary>
/// specs/080 — one real host for the whole class with the settings repository, the profile lookup and the unit
/// of work replaced, so no test writes the shared database. Each test calls <see cref="Reset"/> and stubs what it needs.
/// </summary>
public sealed class AppearanceApiFactory : CustomWebApplicationFactory
{
    public IPresenceSphereSettingsRepository Settings { get; } = Substitute.For<IPresenceSphereSettingsRepository>();

    public IUserProfileRepository Profiles { get; } = Substitute.For<IUserProfileRepository>();

    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

    public void Reset()
    {
        Settings.ClearSubstitute(ClearOptions.All);
        Profiles.ClearSubstitute(ClearOptions.All);
        UnitOfWork.ClearSubstitute(ClearOptions.All);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPresenceSphereSettingsRepository>();
            services.AddSingleton(Settings);
            services.RemoveAll<IUserProfileRepository>();
            services.AddSingleton(Profiles);
            services.RemoveAll<IUnitOfWork>();
            services.AddSingleton(UnitOfWork);
        });
    }
}
