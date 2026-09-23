namespace AskLucy.Application.CustomModels.Abstractions;

/// <summary>
/// FR-019. The deployment target isn't configured on this server. Maps to 400 Problem Details with
/// <c>type …/deployment-not-configured</c>. The message never says which setting is missing or what
/// any configured value is.
/// </summary>
public sealed class CustomModelDeploymentNotConfiguredException()
    : Exception("Deployment isn't configured on this server. Ask a server administrator to configure the deployment target.");
