namespace AskLucy.Application.CustomModels.Abstractions;

/// <summary>specs/072 research D2. Opens one upload session (one connection) per deployment job.</summary>
public interface IDeploymentFileUploader
{
    /// <exception cref="DeploymentTargetException">The server refused the connection, the login or TLS.</exception>
    Task<IDeploymentUploadSession> OpenSessionAsync(DeploymentTargetSettings settings, CancellationToken cancellationToken = default);
}
