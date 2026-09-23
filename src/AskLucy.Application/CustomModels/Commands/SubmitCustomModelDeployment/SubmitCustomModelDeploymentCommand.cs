using MediatR;

namespace AskLucy.Application.CustomModels.Commands.SubmitCustomModelDeployment;

/// <summary>
/// specs/072 contracts/admin-custom-models.md <c>POST api/v1/admin/custom-models</c>. Saves the
/// record and queues the deployment job; never waits on a transfer (FR-012).
/// </summary>
public sealed record SubmitCustomModelDeploymentCommand(string Source, string Destination, string? Name) : IRequest<SubmittedCustomModelDto>;

/// <summary>The summary plus the file path a <c>/resolve/…</c> or <c>/blob/…</c> URL named, which is ignored (the whole repository is deployed).</summary>
public sealed record SubmittedCustomModelDto : CustomModelSummaryDto
{
    public SubmittedCustomModelDto(CustomModelSummaryDto summary, string? ignoredFilePath)
        : base(summary)
    {
        IgnoredFilePath = ignoredFilePath;
    }

    public string? IgnoredFilePath { get; init; }
}
