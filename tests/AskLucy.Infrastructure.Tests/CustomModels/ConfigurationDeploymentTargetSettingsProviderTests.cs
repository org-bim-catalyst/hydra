using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Infrastructure.CustomModels.Deployment;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AskLucy.Infrastructure.Tests.CustomModels;

/// <summary>specs/072 T026 — the temporary config-backed swap point (research D1, FR-017, FR-019).</summary>
public sealed class ConfigurationDeploymentTargetSettingsProviderTests
{
    private const string SecretPassword = "s3cr3t-P@ss-do-not-print";

    private static readonly FtpOptions Valid = new()
    {
        Host = "ftp.example.test",
        Port = 990,
        Username = "deployer",
        Password = SecretPassword,
        RootPath = "/hydra",
        AllowPlainFtp = false,
    };

    [Fact]
    public async Task Valid_options_are_mapped()
    {
        var provider = new ConfigurationDeploymentTargetSettingsProvider(new MutableOptionsMonitor<FtpOptions>(Valid));

        var settings = await provider.GetAsync(TestContext.Current.CancellationToken);

        settings.Should().Be(new DeploymentTargetSettings("ftp.example.test", 990, "deployer", SecretPassword, "/hydra", false));
    }

    [Fact]
    public async Task Surrounding_whitespace_is_trimmed()
    {
        var options = Valid with { Host = "  ftp.example.test ", Username = " deployer ", RootPath = " /hydra " };
        var provider = new ConfigurationDeploymentTargetSettingsProvider(new MutableOptionsMonitor<FtpOptions>(options));

        var settings = await provider.GetAsync(TestContext.Current.CancellationToken);

        settings!.Host.Should().Be("ftp.example.test");
        settings.Username.Should().Be("deployer");
        settings.RootPath.Should().Be("/hydra");
    }

    public static TheoryData<string> NotConfiguredCases => new() { "host-blank", "username-blank", "root-blank", "root-relative", "all-blank" };

    [Theory]
    [MemberData(nameof(NotConfiguredCases))]
    public async Task Missing_or_invalid_settings_mean_not_configured(string @case)
    {
        var options = @case switch
        {
            "host-blank" => Valid with { Host = "  " },
            "username-blank" => Valid with { Username = "" },
            "root-blank" => Valid with { RootPath = "" },
            "root-relative" => Valid with { RootPath = "hydra" },
            _ => new FtpOptions(),
        };
        var provider = new ConfigurationDeploymentTargetSettingsProvider(new MutableOptionsMonitor<FtpOptions>(options));

        var settings = await provider.GetAsync(TestContext.Current.CancellationToken);

        settings.Should().BeNull();
    }

    [Fact]
    public void Neither_ToString_prints_the_password()
    {
        var settings = new DeploymentTargetSettings("ftp.example.test", 990, "deployer", SecretPassword, "/hydra", false);

        Valid.ToString().Should().NotContain(SecretPassword).And.Contain("Password = ***");
        settings.ToString().Should().NotContain(SecretPassword).And.Contain("Password = ***");
    }

    [Fact]
    public async Task A_configuration_change_is_picked_up_without_rebuilding()
    {
        var monitor = new MutableOptionsMonitor<FtpOptions>(new FtpOptions());
        var provider = new ConfigurationDeploymentTargetSettingsProvider(monitor);

        (await provider.GetAsync(TestContext.Current.CancellationToken)).Should().BeNull();

        monitor.CurrentValue = Valid;

        (await provider.GetAsync(TestContext.Current.CancellationToken))!.Host.Should().Be("ftp.example.test");
    }

    private sealed class MutableOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; set; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
