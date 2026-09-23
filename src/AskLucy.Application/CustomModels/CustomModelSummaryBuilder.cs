using AskLucy.Application.Abstractions;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Domain.CustomModels;

namespace AskLucy.Application.CustomModels;

/// <summary>
/// specs/072. Turns <see cref="CustomModel"/> records into the API/hub summary, resolving each
/// submitter's display name once per call. Admin lists are small (research D13), so a lookup per
/// distinct submitter is cheaper than a dedicated batch query. <c>BacksEngine</c> names the
/// registered on-server engine whose repository the model was deployed from, matched ignoring case.
/// </summary>
public sealed class CustomModelSummaryBuilder(IUserAdminRepository users, IEnumerable<IHostedModelEngine> hostedEngines)
{
    public const string UnknownUserDisplayName = "Unknown user";

    public async Task<CustomModelSummaryDto> BuildAsync(CustomModel model, CancellationToken cancellationToken = default) =>
        (await BuildAsync([model], cancellationToken))[0];

    public async Task<IReadOnlyList<CustomModelSummaryDto>> BuildAsync(IReadOnlyList<CustomModel> models, CancellationToken cancellationToken = default)
    {
        var submitters = new Dictionary<string, CustomModelUserDto>(StringComparer.Ordinal);
        foreach (var userId in models.Select(m => m.SubmittedByUserId).Distinct(StringComparer.Ordinal))
        {
            submitters[userId] = await ResolveUserAsync(userId, cancellationToken);
        }

        return models.Select(m => m.ToSummaryDto(submitters[m.SubmittedByUserId], FindBackedEngine(m.RepositoryId))).ToList();
    }

    private string? FindBackedEngine(string repositoryId) =>
        hostedEngines.FirstOrDefault(e => string.Equals(e.ModelRepositoryId, repositoryId, StringComparison.OrdinalIgnoreCase))?.EngineName;

    private async Task<CustomModelUserDto> ResolveUserAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return new CustomModelUserDto(userId, UnknownUserDisplayName);
        }

        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        return new CustomModelUserDto(userId, fullName.Length > 0 ? fullName : user.Email);
    }
}
