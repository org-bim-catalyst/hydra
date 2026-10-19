# Research: Editable Provider Health-Check Retention

**Feature**: 082-editable-health-retention | **Date**: 2026-10-19

## R1 — Storage shape for the setting

**Decision**: A single-row `ProviderHealthRetentionSettings` table keyed by a fixed `Guid` singleton id, deriving `BaseEntity` (audit columns + `rowversion`). The row exists only via the migration seed; the repository exposes `GetAsync` and `ExpectRowVersion`, mirroring `LocalizationSetting` (specs/067 US8). No create path is implemented — a missing row is a recovery-only state, not a normal flow.

**Rationale**: Two existing single-row admin settings already use this exact pattern, including `If-Match` optimistic concurrency in the localization case. Reusing it keeps the codebase uniform and avoids a generic key/value settings table that no current requirement needs.

**Alternatives considered**:
- Generic `Settings(Key, Value)` table — rejected: YAGNI, and it would weaken typing and concurrency semantics for one row.
- Keep configuration-only — rejected: the request is to remove the deployment step.
- Per-provider retention — rejected: the request and UI describe one platform-wide window.

## R2 — Seed value in the migration

**Decision**: Seed the singleton row in `HasData` with `RetentionDays = 30`, a fixed `CreatedAtUtc`, and `CreatedBy = "system"`.

**Rationale**: `ProviderHealthCheck:RetentionDays` has no override in the repository (`appsettings.json` sets only `Interval`), so the effective value at release is the options default of 30 days. EF migrations cannot read runtime `IConfiguration`, so the seed is a literal; the value is documented next to the seed so a deployment that has an override can adjust it before applying. A stored value takes precedence from then on.

**Alternatives considered**:
- Startup seeder reading configuration — rejected: adds a second write path and races the migration; the request explicitly asks for seeding in the migration.
- Raw SQL in the migration reading a config table — rejected: configuration is not in the database.

## R3 — Fallback precedence and where it lives

**Decision**: Stored value wins; `ProviderHealthCheckOptions.RetentionDays` is used only when the singleton row is missing. Move `ProviderHealthCheckOptions` from `Infrastructure/Ai` to `Application/Ai` so the query handler and the retention job can share the same options type without Application referencing Infrastructure. The job keeps its existing options dependency and adds the repository.

**Rationale**: Both the GET response and the daily job must state the same precedence. Keeping the options class in Infrastructure would either force an illegal Application→Infrastructure reference or duplicate the rule in the controller. Options classes already live in Application elsewhere (for example `AgentRuntimeOptions`).

**Alternatives considered**:
- Controller composes fallback from options — rejected: puts a business rule in presentation and splits it from the job's copy.
- New `IProviderHealthRetentionFallback` abstraction — rejected: one more interface for a value already available through options.

## R4 — Concurrency contract

**Decision**: `PUT` requires `If-Match` carrying the base64 row version returned by `GET`; missing header → 428, stale version → 409 through `ConcurrencyConflictException`. This copies `AdminLocalizationController` and `UpdateLocalizationSettingsCommandHandler`.

**Rationale**: The requirement explicitly names `If-Match` rowVersion. The pattern is already proven in this repository and maps to Problem Details without a 500.

**Alternatives considered**:
- Last-write-wins (as in specs/080 presence sphere) — rejected: the requirement asks for an edit-conflict guard.
- `If-Unmodified-Since` — rejected: no timestamp semantics needed; rowversion already exists.

## R5 — Audit trail

**Decision**: Add a dedicated append-only `ProviderHealthRetentionAuditLogs` table (`ActorUserId`, `PreviousRetentionDays`, `NewRetentionDays`, `OccurredAtUtc`, correlation id) written in the same unit of work as the setting change. The repository exposes `AddAudit`, and the existing `AuditSaveChangesInterceptor` stamps audit columns and correlation id.

**Rationale**: The platform's audit model is one append-only log per domain area (`RoleAuditLog`, `NotificationAuditLog`, `McpAuditLog`, …). There is no generic admin-audit table. Reusing `NotificationAuditLog` would surface provider-health changes in the notification hub's audit view, which is semantically wrong. A small dedicated table keeps the record queryable and durable without exposing it in an unrelated area.

**Alternatives considered**:
- Reuse `NotificationAuditLog` — rejected: cross-area pollution of the notification audit feature and its API.
- Structured logging only — rejected: not a durable, governance-grade trail and not what "audit logging" means elsewhere in this codebase.
- A generic `AdminAuditLog` table — rejected: YAGNI for a single setting; can replace this later if more settings need one.

## R6 — Validation limits

**Decision**: 1–365 days, enforced in the domain entity (`Update` throws on out-of-range) and in a FluentValidation validator (`InclusiveBetween`), producing a 400 Problem Details response (the platform's standard FluentValidation mapping).

**Rationale**: At least one day of history keeps the setting meaningful; 365 days bounds accidental permanent retention. Domain + validator duplication is the existing pattern for single-row settings (specs/080) and keeps the rule safe if a handler is added later.

**Alternatives considered**:
- 7–180 days — rejected: no evidence for those bounds, and a three-month maximum would be arbitrary.
- No upper bound — rejected: an accidental large value silently disables cleanup.

## R7 — Frontend approach

**Decision**: Extend the existing `/admin/provider-health` page in place. Keep the current read-only summary and add a numeric field plus Save. Use TanStack Query `useQuery`/`useMutation`, update the cache with the PUT response, and show a success Snackbar. Surface 409 with a "reload" affordance and a validation message naming the allowed range. Gate editing on `useIsAdmin() || useCan('admin.operational-failures.manage')`, following `useCanManageNotifications`.

**Rationale**: The page, route and nav entry already exist, so there is no new navigation surface. The admin operational-failures area is not localized yet; the page's existing strings are hardcoded English, so new strings follow suit rather than adding a half-used namespace. A single numeric field does not justify React Hook Form/Zod; the nearest admin settings page (`AdminLocalizationPage`) uses local state.

**Alternatives considered**:
- New page or route — rejected: the requirement says to add the control to the existing page.
- React Hook Form + Zod — rejected: one field, and the established sibling page uses local state.
- New i18n namespace — rejected: the surface is English-only today; adding a partial namespace would not localize the page.

## R8 — Retention job behaviour

**Decision**: `ProviderHealthCheckRetentionJob` reads the singleton row per run inside its Hangfire scope; if the row is null it uses `ProviderHealthCheckOptions.RetentionDays`. It does not catch database exceptions: a failed read means the database is unavailable, and the delete would fail too — Hangfire records the failed run and retries the next day.

**Rationale**: One row read per day is negligible. Swallowing database failures would hide a real outage. "Fallback" is defined as "row missing", not "database broken".

**Alternatives considered**:
- Cache the value in process — rejected: unnecessary for a daily job; stale caching would contradict "next run uses the new value".
- Catch-all fallback — rejected: would hide outages and make the job silently stop deleting.

## R9 — Migration and test database

**Decision**: One reversible migration adds both tables and seeds the singleton. After generating it, apply it to the shared test database used by `PERSISTENCE_TESTS_2_CONNECTION_STRING`/`PERSISTENCE_TESTS_CONNECTION_STRING`, per this repository's convention (CI runs persistence tests against an already-migrated database).

**Rationale**: Repository convention (see CLAUDE.md and prior specs); without the manual migration the Web.Tests host fails at startup.

**Alternatives considered**: None — this is the repository's established process.

## Resolved unknowns

The Technical Context contains no `NEEDS CLARIFICATION` items. The spec's only open defaults (range, permissions, audit store, missing-row behaviour) are resolved above and recorded in the spec's Assumptions.
