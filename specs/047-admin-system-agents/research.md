# Phase 0 Research: Admin Visibility of System Agents

No `NEEDS CLARIFICATION` markers were left in the Technical Context — this feature reuses
established patterns end-to-end. The decisions below record *why* each existing pattern was
chosen over alternatives, for a feature this small.

## Decision 1: Authorization policy

**Decision**: Reuse the existing `"AdministratorOrSuperUser"` ASP.NET Core authorization policy,
applied the same way `AdminAiProvidersController` already does:
`[Authorize(Policy = "AdministratorOrSuperUser")]`.

**Rationale**: The spec (FR-005) requires exactly this pair of roles, and this policy already
exists, is already tested (`RoleAuthorizationTests`), and already gates a structurally identical
admin-only, read-mostly controller. Introducing a second, differently-named policy with the same
effective rule would violate constitution §III (YAGNI) and §VII (Convention Over Configuration).

**Alternatives considered**: A brand-new `"SystemAgentsAdmin"` policy — rejected, no behavioral
difference from the existing policy, pure duplication.

## Decision 2: Query/repository shape

**Decision**: Add one new read method to the existing `IAgentRepository` —
`ListSystemOwnedAsync(CancellationToken)` — returning all agents where `IsSystemOwned == true`,
with their `Versions` loaded so the handler can read the newest version's `CreatedAtUtc`/`VersionNumber`
without a second round trip. Expose it via a new `GetSystemAgentsQuery` (MediatR), following the
exact shape of the existing `GetAdminAiProvidersQuery` → `IAgentRepository`-analog pattern.

**Rationale**: Constitution §3 requires aggregate-oriented repository methods, not a generic
`IQueryable` leak into Application code. `ListByOwnerAsync` already establishes the convention of
a purpose-named repository method per list shape; `ListSystemOwnedAsync` extends that same
convention rather than introducing a new one. The existing `Agent.PublishedVersionNumber` and
`BaseEntity.ModifiedAtUtc`/`CreatedAtUtc` fields already carry every value FR-002 needs — no new
Domain field or migration is required.

**Alternatives considered**:
- Looking up each of the five known `SystemAgentDefinitions.All` keys individually via the
  existing `GetBySystemKeyAsync` — rejected: it would leak a static Application-layer list into
  what should be a database-truth read, and would silently miss a system agent if one were ever
  provisioned outside that static list (defeats the purpose of an operational visibility check).
- A raw `IQueryable` filter exposed to Application — rejected per constitution §3's explicit
  prohibition.

## Decision 3: Pagination

**Decision**: No pagination — return the full list in one response.

**Rationale**: Constitution §6 requires list endpoints to be paginated by default but explicitly
carves out "small stable admin lists" as offset-based-acceptable; the existing
`GetAdminAiProvidersQuery` (a structurally identical admin list) already sets this precedent
unpaginated. System agents are defined in a single static file (`SystemAgentDefinitions.All`,
currently 5 entries) and are not expected to grow into the hundreds — the spec's own Assumptions
section states this explicitly.

**Alternatives considered**: Cursor pagination matching `ListByOwnerAsync` — rejected as
overhead disproportionate to a list that will realistically stay in the single digits to low
tens for years.

## Decision 4: Frontend placement

**Decision**: New page `AdminSystemAgentsPage.tsx` under `features/admin/pages/`, registered in
`adminNav.tsx` and lazy-loaded in `routes/router.tsx`, at `/admin/system-agents` — following the
exact registration mechanics already used for `/admin/ai-providers`.

**Rationale**: Constitution §7 requires new UI to compose from the existing design system and
established feature-domain folder structure before introducing anything bespoke; this feature
needs nothing beyond a data table and a read-only status/system-owned badge, both of which
already exist in the Agent Library UI (T108, specs/045).

**Alternatives considered**: Folding this into the existing personal Agents page behind a
toggle — rejected explicitly by the spec (FR-007: the personal page's scoping must not change)
and by the user's own framing of this as a *separate* admin view.
