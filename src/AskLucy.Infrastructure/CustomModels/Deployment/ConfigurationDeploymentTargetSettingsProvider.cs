using AskLucy.Application.CustomModels.Abstractions;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.CustomModels.Deployment;

/// <summary>
/// TEMPORARY — specs/072 plan.md "Deliberately temporary design". Reads the deployment target
/// from the <c>Ftp</c> configuration section until spec 071 Connectors replaces this one class
/// with a connector-backed <see cref="IDeploymentTargetSettingsProvider"/>. It is the only reader of
/// <see cref="FtpOptions"/>. Reads the monitor on every call, so a config edit applies to the next
/// deployment without a restart.
/// </summary>
public sealed class ConfigurationDeploymentTargetSettingsProvider(IOptionsMonitor<FtpOptions> options)
    : IDeploymentTargetSettingsProvider
{
    public ValueTask<DeploymentTargetSettings?> GetAsync(CancellationToken cancellationToken = default)
    {
        var ftp = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(ftp.Host)
            || string.IsNullOrWhiteSpace(ftp.Username)
            || string.IsNullOrWhiteSpace(ftp.RootPath)
            || !ftp.RootPath.Trim().StartsWith('/'))
        {
            return ValueTask.FromResult<DeploymentTargetSettings?>(null);
        }

        return ValueTask.FromResult<DeploymentTargetSettings?>(new DeploymentTargetSettings(
            ftp.Host.Trim(),
            ftp.Port,
            ftp.Username.Trim(),
            ftp.Password,
            ftp.RootPath.Trim(),
            ftp.AllowPlainFtp));
    }
}
