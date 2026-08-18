using MediatR;

namespace AskLucy.Application.Memory.Queries.GetMemoryPreferences;

public sealed record MemoryCategoryPreferenceDto(string Category, string ApprovalMode, bool IsEnabled);

/// <summary>contracts/memory-privacy-api.md — `GET /api/v1/memories/preferences` (spec.md FR-007, FR-022, FR-025).</summary>
public sealed record MemoryPreferencesDto(bool MemoryEnabled, IReadOnlyList<MemoryCategoryPreferenceDto> Categories);

public sealed record GetMemoryPreferencesQuery : IRequest<MemoryPreferencesDto>;
