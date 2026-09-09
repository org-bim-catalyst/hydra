# Phase 1 Data Model: Admin Visibility of System Agents

No new entities, fields, or migrations. This feature is a read-only projection over existing
`Agent`/`AgentVersion` data (specs/020-ai-agent-framework, extended by specs/045 for system
agents). Documented here for traceability against the spec's Key Entities section.

## Existing entities read (no changes)

### `Agent` (`src/AskLucy.Domain/Agents/Agent.cs`)

Fields this feature reads:

| Field | Type | Used for |
|---|---|---|
| `Id` | `Guid` | Row identity |
| `Name` | `string` | FR-002 display name |
| `Status` | `AgentStatus` (Draft/Published/Archived) | FR-002 lifecycle status |
| `IsSystemOwned` | `bool` | Filter predicate (`ListSystemOwnedAsync`) and FR-004 badge |
| `SystemKey` | `string?` | Stable identifier for display/debugging (e.g. `lucy.orchestrator`) |
| `PublishedVersionNumber` | `int?` | FR-002 current version number |
| `Versions` (`IReadOnlyCollection<AgentVersion>`) | — | Source of the newest version's timestamp |
| `ModifiedAtUtc` / `CreatedAtUtc` (from `BaseEntity`) | `DateTime?` / `DateTime` | Fallback last-updated timestamp if version history is unavailable |

Filter: `IsSystemOwned == true` (equivalently `OwnerId == Agent.SystemOwnerId`, the `"system"`
sentinel — `IsSystemOwned` is the intention-revealing predicate already exposed for this exact
purpose).

### `AgentVersion` (`src/AskLucy.Domain/Agents/AgentVersion.cs`)

Fields this feature reads (from the newest entry in `Agent.Versions`, ordered by `VersionNumber`
descending):

| Field | Type | Used for |
|---|---|---|
| `VersionNumber` | `int` | Cross-check against `Agent.PublishedVersionNumber` |
| `CreatedAtUtc` (from `BaseEntity`) | `DateTime` | FR-002 "last provisioned/update" timestamp — this is the moment `SystemAgentProvisioner` last published a new version, which is the more precise operational signal than `Agent.ModifiedAtUtc` |

## New read model (DTO, not persisted)

### `AdminSystemAgentDto` (`src/AskLucy.Application/Agents/Queries/GetSystemAgents/AdminSystemAgentDto.cs`)

```
record AdminSystemAgentDto(
    Guid Id,
    string Name,
    string? SystemKey,
    AgentStatus Status,
    int? PublishedVersionNumber,
    DateTime LastUpdatedAtUtc);
```

`LastUpdatedAtUtc` = newest `Versions` entry's `CreatedAtUtc` if any version exists, else
`Agent.ModifiedAtUtc ?? Agent.CreatedAtUtc` (an edge case: a system agent record exists but has
never been published — should not occur in practice since `SystemAgentProvisioner` always
publishes on create, but the fallback keeps the projection total rather than throwing).

## State/lifecycle

No new state transitions. `Agent.Status` already transitions via existing `Agent` methods
(`Publish`, `Archive`, `Restore`) exercised by specs/020/045 — this feature only reads the
current value, never writes it (FR-003: read-only).
