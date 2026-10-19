# Data Model: Editable Provider Health-Check Retention

**Feature**: 082-editable-health-retention | **Date**: 2026-10-19

## Entity: ProviderHealthRetentionSetting

The single platform-wide retention window. One row, fixed id, never soft-deleted.

**Table**: `ProviderHealthRetentionSettings`

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `Id` | `uniqueidentifier` | PK, not generated | Fixed singleton id constant on the entity |
| `RetentionDays` | `int` | required; 1–365 enforced in the domain | Days of health-check history to keep |
| `CreatedAtUtc` | `datetime2` | required | Stamped by `AuditSaveChangesInterceptor` / seed |
| `CreatedBy` | `nvarchar(450)` | required | `system` for the seeded row |
| `ModifiedAtUtc` | `datetime2` | nullable | Stamped on update |
| `ModifiedBy` | `nvarchar(450)` | nullable | Actor user id |
| `DeletedAtUtc` | `datetime2` | nullable | Never set by this feature |
| `DeletedBy` | `nvarchar(max)` | nullable | Never set by this feature |
| `RowVersion` | `rowversion` | required | Optimistic concurrency token used by `If-Match` |

**Domain behaviour**

- `Update(int retentionDays, string actor, DateTime utcNow)` — replaces the value; throws `ArgumentOutOfRangeException` outside 1–365, changing nothing.
- No create path: the migration's `HasData` is the only way the row comes into existence. A missing row is a recovery-only state.
- Constants: `MinRetentionDays = 1`, `MaxRetentionDays = 365`, `DefaultRetentionDays = 30`, `SingletonId`.

**Seed** (`HasData`, executed by the migration):

```text
Id              = <fixed singleton guid>
RetentionDays   = 30
CreatedAtUtc    = 2026-10-19T00:00:00Z
CreatedBy       = "system"
```

`30` is the effective `ProviderHealthCheck:RetentionDays` value at the time of release; no override exists in the repository.

## Entity: ProviderHealthRetentionAuditLog

Append-only record of a successful change. No update or delete path.

**Table**: `ProviderHealthRetentionAuditLogs`

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `Id` | `uniqueidentifier` | PK | `Guid.CreateVersion7()` |
| `ActorUserId` | `nvarchar(450)` | required, non-whitespace | Never FK'd; outlives a removed user |
| `PreviousRetentionDays` | `int` | required | The value before the change |
| `NewRetentionDays` | `int` | required | The value after the change |
| `OccurredAtUtc` | `datetime2` | required | Set by the factory |
| `CorrelationId` | `nvarchar(64)` | nullable | Stamped by the interceptor (`ICorrelated`) |
| `CreatedAtUtc` / `CreatedBy` | | required | Stamped by the interceptor |
| `RowVersion` | `rowversion` | required | From `BaseEntity`; never used for concurrency checks |

**Factory**: `Record(string actorUserId, int previousRetentionDays, int newRetentionDays)`.

**Write path**: same unit of work as the setting update, so either both rows commit or neither does. Rejected saves write nothing.

## Entity: ProviderHealthCheck (existing — unchanged)

Append-only health-check history. The retention job deletes rows where `CheckedAtUtc < now - retentionDays`. No schema change.

## Relationships

None. The setting and its audit log are platform-wide; neither references users or providers by FK.

## Indexes

- `ProviderHealthRetentionSettings`: PK only (one row).
- `ProviderHealthRetentionAuditLogs`: PK only. No read endpoint or query path exists in this feature; add an `OccurredAtUtc` index only if an audit list is introduced later.
- `ProviderHealthChecks`: unchanged. The existing `IX_ProviderHealthChecks_ProviderId_CheckedAtUtc` does not lead with `CheckedAtUtc`, so the daily time-only delete scans; accepted while the table is small (documented in `docs/DATABASE.md`).

## State

The setting has no lifecycle beyond create (seed) and update. The audit log is insert-only.

## Validation summary

| Rule | Where enforced | Failure |
|------|----------------|---------|
| Whole number | Request binding / FluentValidation | 400 |
| 1 ≤ days ≤ 365 | Domain `Update` + FluentValidation | 400 |
| `If-Match` present and base64 | Controller | 428 |
| Row version matches | Handler / EF concurrency token | 409 |
| Actor present | `ICurrentUserAccessor` (authenticated admin) | 401/403 by the authorization pipeline |
