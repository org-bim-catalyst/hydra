namespace AskLucy.Web.Contracts;

public sealed record BulkTargetRequest(IReadOnlyList<string>? Ids, bool AllMatching, string? Search = null);

public sealed record BulkActionResultResponse(int SucceededCount, IReadOnlyList<BulkActionSkipResponse> Skipped);

public sealed record BulkActionSkipResponse(string Id, string Reason);

public sealed record BulkEligibleIdsResponse(IReadOnlyList<string> Ids);

public sealed record BulkDeleteRolesResultResponse(
    int SucceededCount, IReadOnlyList<BulkActionSkipResponse> Skipped, IReadOnlyDictionary<string, int> UnassignedUserCounts);

public sealed record BulkAssignRoleRequest(string RoleId, IReadOnlyList<string>? Ids, bool AllMatching, string? Search = null);
