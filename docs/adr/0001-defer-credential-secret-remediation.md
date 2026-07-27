# ADR-0001: Defer credential/secret remediation from the legacy modernization migration

**Status**: Accepted
**Date**: 2026-07-27
**Deciders**: Product stakeholder (via `/speckit-specify` clarification session for SPEC-000)

## Context

While assessing the legacy application for `specs/000-legacy-modernization/spec.md`, the
following were found already committed to source control and tracked in git history:

- A hardcoded plaintext seed-admin password, baked into `ChatGPT_ClientContext`'s EF Core
  migrations (`OnModelCreating` → `SeedUsers`).
- Live OAuth (Google/Facebook), OpenAI, SendGrid, and SQL Server connection-string
  secrets in `appsettings.json`.
- Three credentialed `*.PublishSettings` files at the repository root.

This directly conflicts with constitution §8 ("Secrets MUST NOT be stored... in
configuration files committed to the repository") and §22 of `docs/SECURITY.md`.

## Decision

**These are explicitly not remediated by SPEC-000.** When offered as the recommended
option during `/speckit-specify`, the stakeholder chose to keep the existing exposure as
an accepted risk for this migration ("keep it as it is for now"), scoping this spec to
architectural/behavioral parity rather than a security-remediation project.

New code written during this migration does not perpetuate the pattern: `AskLucyDbContext`
contains no `HasData()` seed-admin credential, and all new secrets (JWT signing key,
OpenAI API key, SendGrid key, connection string) are read from environment
variables/user-secrets, never committed (see `src/AskLucy.WebAPI/appsettings.json`'s
`_comment_secrets` note).

## Consequences

- The already-exposed credentials remain exposed to anyone with repository access until
  a separate, dedicated remediation task rotates them and removes the seed-admin
  migration.
- This is tracked as an accepted, documented risk in `spec.md` § Risks — not a silently
  dropped finding.
- **Follow-up required**: a dedicated security-remediation task, owned separately from
  this migration, must rotate the exposed credentials (OAuth secrets, OpenAI keys,
  SendGrid key, database password) and strip the hardcoded seed-admin password from the
  legacy migration history before or shortly after this migration reaches production.
  No owner/date has been assigned yet — this must be tracked before the legacy project
  is decommissioned (T065).

## Alternatives considered

- **Rotate and remediate as part of this migration** (the recommended option offered) —
  rejected by the stakeholder to keep this migration's scope to architecture/behavior
  parity.
