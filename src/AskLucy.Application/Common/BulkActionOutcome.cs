namespace AskLucy.Application.Common;

public sealed record BulkActionSkip(string Id, string Reason);

public sealed record BulkActionOutcome(int SucceededCount, IReadOnlyList<BulkActionSkip> Skipped);
