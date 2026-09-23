using System.Text;

namespace AskLucy.Application.CustomModels.Abstractions;

/// <summary>
/// specs/072 research D1. Where and how to upload a deployment, independent of where the settings
/// came from. <b>This is the Connectors swap point</b>: today it is filled from configuration by the
/// deliberately temporary <c>ConfigurationDeploymentTargetSettingsProvider</c>; a future Connectors
/// feature replaces only that provider. <see cref="ToString"/> never prints <see cref="Password"/>,
/// and nothing may log, return or put <see cref="Password"/>, <see cref="Host"/> or
/// <see cref="RootPath"/> in an exception message.
/// </summary>
public sealed record DeploymentTargetSettings(
    string Host,
    int Port,
    string Username,
    string Password,
    string RootPath,
    bool AllowPlainFtp)
{
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
