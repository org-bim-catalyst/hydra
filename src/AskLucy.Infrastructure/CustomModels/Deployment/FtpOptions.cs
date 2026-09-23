using System.Text;

namespace AskLucy.Infrastructure.CustomModels.Deployment;

/// <summary>
/// The <c>Ftp</c> configuration section. TEMPORARY until spec 071 Connectors — see specs/072 plan.md.
/// Only <see cref="ConfigurationDeploymentTargetSettingsProvider"/> reads it; everything else goes
/// through <c>IDeploymentTargetSettingsProvider</c>. The password is redacted from <see cref="ToString"/>
/// and must never be logged, returned, or put in an exception message.
/// </summary>
public sealed record FtpOptions
{
    public const string SectionName = "Ftp";

    /// <summary>Blank means deployment is not configured (FR-019).</summary>
    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 21;

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    /// <summary>For example <c>/hydra</c>. Must start with <c>/</c>.</summary>
    public string RootPath { get; init; } = string.Empty;

    /// <summary>FR-018a. A conscious downgrade to unencrypted FTP; logged as a warning at the start of every job.</summary>
    public bool AllowPlainFtp { get; init; }

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Host = ").Append(Host)
            .Append(", Port = ").Append(Port)
            .Append(", Username = ").Append(Username)
            .Append(", Password = ***")
            .Append(", RootPath = ").Append(RootPath)
            .Append(", AllowPlainFtp = ").Append(AllowPlainFtp);
        return true;
    }
}
