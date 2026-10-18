using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;

namespace AskLucy.Application.Ai.Deprecation;

/// <summary>
/// specs/069 FR-018/FR-020b: everyone who administers AI providers receives the deprecation
/// summary. A Super User may turn its email off; everyone else, including a user who holds both
/// roles, may not, so the recipients are split into two groups.
/// </summary>
public sealed class AdminRecipientResolver(
    IRoleRepository roles,
    IRoleAssignmentRepository assignments)
{
    private const string ProviderAdministrationPermission = "admin.ai-providers.manage";

    public async Task<SummaryRecipients> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var qualifyingRoleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in await roles.ListByPermissionAsync(ProviderAdministrationPermission, cancellationToken))
        {
            qualifyingRoleIds.Add(role.Id);
        }

        var administratorRole = await roles.GetByNormalizedNameAsync(
            PrivilegedRoleNames.Administrator.ToUpperInvariant(), cancellationToken);
        var superUserRole = await roles.GetByNormalizedNameAsync(
            PrivilegedRoleNames.SuperUser.ToUpperInvariant(), cancellationToken);
        if (administratorRole is not null)
        {
            qualifyingRoleIds.Add(administratorRole.Id);
        }

        if (superUserRole is not null)
        {
            qualifyingRoleIds.Add(superUserRole.Id);
        }

        var holders = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var roleId in qualifyingRoleIds)
        {
            foreach (var userId in await assignments.ListUserIdsByRoleAsync(roleId, cancellationToken))
            {
                if (!holders.TryGetValue(userId, out var held))
                {
                    holders[userId] = held = [];
                }

                held.Add(roleId);
            }
        }

        var mandatory = new List<string>();
        var optional = new List<string>();
        foreach (var (userId, held) in holders)
        {
            var superUserOnly = superUserRole is not null
                && held.Contains(superUserRole.Id)
                && held.Count == 1;
            (superUserOnly ? optional : mandatory).Add(userId);
        }

        return new SummaryRecipients(mandatory, optional);
    }
}

public sealed record SummaryRecipients(IReadOnlyList<string> Mandatory, IReadOnlyList<string> Optional);
