using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.Options;
using MediatR;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.CustomModels.Queries.GetDeploymentStatus;

public sealed class GetDeploymentStatusQueryHandler(
    IDeploymentTargetSettingsProvider deploymentTarget,
    IOptionsMonitor<CustomModelsOptions> options) : IRequestHandler<GetDeploymentStatusQuery, DeploymentStatusDto>
{
    public const string SecureTransport = "FTPS";
    public const string PlainTransport = "FTP";

    public async Task<DeploymentStatusDto> Handle(GetDeploymentStatusQuery request, CancellationToken cancellationToken)
    {
        var settings = await deploymentTarget.GetAsync(cancellationToken);
        var current = options.CurrentValue;

        return new DeploymentStatusDto(
            IsConfigured: settings is not null,
            Transport: settings is null ? null : settings.AllowPlainFtp ? PlainTransport : SecureTransport,
            current.MaxDeploymentBytes,
            current.GetAllowedDestinationPrefixes());
    }
}
