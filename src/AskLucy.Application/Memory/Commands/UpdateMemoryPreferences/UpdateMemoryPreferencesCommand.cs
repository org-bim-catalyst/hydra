using AskLucy.Domain.Memory;
using MediatR;

namespace AskLucy.Application.Memory.Commands.UpdateMemoryPreferences;

/// <summary>contracts/memory-privacy-api.md — a partial update: only categories present in <see cref="Categories"/> change; omitted categories keep their current settings.</summary>
public sealed record MemoryCategoryPreferenceUpdate(MemoryCategory Category, MemoryApprovalMode? ApprovalMode, bool? IsEnabled);

/// <summary>`PUT /api/v1/memories/preferences` (spec.md FR-007, FR-022, FR-025).</summary>
public sealed record UpdateMemoryPreferencesCommand(bool? MemoryEnabled, IReadOnlyList<MemoryCategoryPreferenceUpdate> Categories) : IRequest;
