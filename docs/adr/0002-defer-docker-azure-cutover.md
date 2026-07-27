# ADR-0002: Defer Docker containerization and Azure App Service cutover

**Status**: Accepted
**Date**: 2026-07-27
**Deciders**: Product stakeholder (via `/speckit-specify` clarification session for SPEC-000)

## Context

The org's longer-term target architecture (`docs/ARCHITECTURE.md`) and the constitution's
CI/CD article (§12: "Docker images are built for backend and frontend on merge to
`master`") both expect Docker containerization, with Azure App Service as the deployment
target. The legacy application is currently deployed via FTP/MSDeploy to a third-party
shared host (`site4now.net`), plus an orphaned Azure App Service still named
`chatgpt-client`.

## Decision

**This migration does not adopt Docker or cut over to Azure.** When offered the
recommended option ("re-platform to Docker + GitHub Actions + Azure App Service") during
`/speckit-specify`, the stakeholder explicitly chose to keep the existing `site4now.net`
hosting target: "keep the current deployment and ignore docker. Just site4now and Github
actions for CI/CD."

`.github/workflows/ci.yml`'s `deploy` job therefore publishes `src/AskLucy.WebAPI`
directly and deploys via FTP to `site4now.net`, with no `Dockerfile` and no Azure
resources introduced.

## Consequences

- This is a knowing, deliberate divergence from constitution §12 for this phase only —
  recorded here per the constitution's own Governance requirement that deviations be
  justified via ADR rather than silently introduced (see `plan.md` § Complexity Tracking,
  which cross-references this ADR).
- CI/CD is still fully automated (FR-029/FR-030, tasks.md T061–T062) — only the
  containerization/hosting-target pieces of the target architecture are deferred, not
  the automation itself.
- The orphaned `chatgpt-client.azurewebsites.net` Azure App Service is untouched; its
  decommissioning (if desired) remains a separate operational task, out of scope here
  (`spec.md` § Assumptions).
- **Follow-up required**: when a future specification revisits hosting/infrastructure,
  it should re-evaluate Docker/Azure adoption starting from this ADR rather than
  re-litigating the FTP-vs-Azure question from scratch. No target date has been set.

## Alternatives considered

- **Re-platform to Docker + Azure App Service now** (the recommended option offered) —
  rejected by the stakeholder to keep this migration's scope focused on
  architecture/behavior parity rather than an infrastructure cutover.
- **Parallel-run/blue-green cutover** — considered in `spec.md` § Migration Strategy as a
  general technique, but not applicable here since the hosting target itself isn't
  changing in this phase.
