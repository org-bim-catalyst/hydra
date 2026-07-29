# ADR-0003: Interim admin security controls for the Admin Dashboard & User Management Console

**Status**: Accepted
**Date**: 2026-07-28
**Deciders**: Product stakeholder (via `/speckit-analyze` review session for SPEC-001)

## Context

While planning `specs/001-admin-dashboard/plan.md`, two gaps against constitution §6/§8
were identified:

1. **§8 Audit trail.** "Authentication events, authorization denials, billing changes,
   and AI agent tool executions MUST be written to an immutable audit trail distinct
   from general application logs." No such immutable audit-trail mechanism exists
   anywhere in this codebase yet, for any feature.
2. **§6 Rate limiting.** "Every public endpoint is subject to rate limiting." The
   existing `GET/PATCH /api/v1/users` endpoints (from SPEC-000) were never rate-limited.

## Decision

**Rate limiting (§6) is resolved, not deferred.** `tasks.md` T057a adds an
`admin-endpoints` rate-limit policy (`src/AskLucy.WebAPI/Program.cs`, mirroring the
existing `ai-endpoints` policy), applied to every endpoint this feature introduces or
touches in `UsersController`/`AdminDashboardController`. This closes the gap in-scope
rather than carrying it forward a second time.

**The immutable audit trail (§8) is accepted as an interim gap, not resolved here.**
Every admin action (lock/unlock/role-change/force-2FA-reset/delete) is logged via
structured Serilog (`AdminActionLog`, named properties: `Action`, `ActorUserId`,
`TargetUserId`) — the same general-purpose log stream every other feature in this
codebase writes to, not a separate immutable store. Building a dedicated audit-trail
store for only this feature's five actions would duplicate effort once a real,
project-wide audit-trail initiative lands, and would still leave authentication events
and authorization denials (already unaudited today, across the whole application)
uncovered. The gap is better tracked and closed holistically, matching ADR-0001's own
precedent of documenting an accepted deviation rather than silently absorbing it.

## Consequences

- Admin action history is discoverable only through the existing structured log sink
  (whatever operational log tooling already consumes Serilog output), not a queryable,
  tamper-evident audit table.
- **Follow-up required**: a project-wide immutable audit-trail initiative (covering
  authentication events, authorization denials, billing changes, AI agent tool
  executions, and this feature's admin actions together) remains an open constitution
  §8 gap. No owner/date has been assigned yet.
- The rate-limiting gap this ADR also considered is fully closed for this feature's
  endpoints (see Decision above) — it does not carry forward to a follow-up.

## Alternatives considered

- **Build a minimal audit table just for this feature** — rejected: five actions'
  worth of bespoke audit storage would be thrown away or awkwardly migrated once a real
  audit-trail initiative covers the rest of the application; better to accept the gap
  explicitly and close it once, project-wide.
- **Defer rate limiting alongside the audit trail** (the original plan.md draft, before
  this ADR) — rejected during `/speckit-analyze` review: unlike the audit trail, closing
  the rate-limiting gap for new endpoints was cheap (mirroring an existing pattern) and
  carrying forward a newly-introduced set of unlimited admin endpoints was accepted as
  unnecessary risk.
