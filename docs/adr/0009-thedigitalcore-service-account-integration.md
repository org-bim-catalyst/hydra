# ADR-0009: TheDigitalCore integration via a single Ask Lucy service account, not per-user credentials or an MCP server

**Status**: Accepted
**Date**: 2026-08-22
**Deciders**: Engineering (SPEC-050 Conversational Park Site Analysis Agent)

## Context

`specs/050-park-site-analysis-agent` needs to search for, create, and relay analysis results into
Projects owned by **TheDigitalCore** — a separate, existing platform that remains the system of
record for Project/Company/Attachment/SiteAnalysis/DesignConcept/DesignRecommendation data
(FR-025). No individual Ask Lucy user has, or should need, their own TheDigitalCore account
(Clarifications Q2, FR-027a).

Constitution §17 requires an ADR before introducing a new cross-cutting infrastructure dependency
or a new architectural pattern not already established in this codebase — a service-account-
authenticated external HTTP integration is both.

## Decision

Add `ITheDigitalCoreClient` (`Application/Abstractions`), implemented in `Infrastructure/
TheDigitalCore` as a named `IHttpClientFactory` client authenticated with a single, dedicated Ask
Lucy service-account credential (`TheDigitalCoreIntegrationOptions`, bound via `IOptions<T>` and
`ValidateOnStart`, sourced from environment/secret manager per constitution §4 — never committed,
never exposed to a browser). All TheDigitalCore API calls this feature makes (search, create,
relay) go through this one client, under this one credential — never a per-user TheDigitalCore
credential, and never modeled as an MCP server.

## Alternatives considered

- **Model TheDigitalCore as an MCP server, like the site-analysis Python tools (ADR-0008)** —
  rejected (`specs/050` research.md Decision 3): MCP's admin-activation-lifecycle semantics
  ("every discovered tool starts inactive until an administrator activates it") are designed for
  optional, discoverable third-party tool sources — TheDigitalCore is neither optional nor
  discoverable for this feature; it is the mandatory system of record the whole feature exists to
  write results into.
- **Require each Ask Lucy user to hold their own TheDigitalCore account/credential** — rejected per
  Clarifications Q2: explicitly ruled out by the stakeholder; would also require a new per-user
  credential-linking UX this feature does not otherwise need.
- **Have TheDigitalCore call Ask Lucy server-to-server instead** — rejected (superseded an earlier
  draft of this spec): the bootstrap flow (FR-001a-FR-001g) requires Ask Lucy's own agent to
  actively search/create in TheDigitalCore mid-conversation, which only works if Ask Lucy is the
  caller.

## Consequences

- A new, single, non-personal service identity exists in Ask Lucy's configuration surface,
  distinct from ordinary end-user JWT authentication (FR-027) — this is a new trust boundary this
  codebase's authorization code must account for (least privilege: this credential should be
  scoped by TheDigitalCore to only the operations this feature needs, per plan.md's §8 Security
  note — enforced on TheDigitalCore's side, not verifiable from this repository).
- TheDigitalCore's actual API surface (contracts/thedigitalcore-integration-api.md) is, as of this
  ADR, a **consumer-driven contract** written from Ask Lucy's side only — it must be reconciled
  with TheDigitalCore's real `ProjectsApiController` (and a new site-analysis-results endpoint,
  which does not exist yet) before this integration can run against a real TheDigitalCore instance.
- If TheDigitalCore's authentication mechanism for service accounts turns out to differ from a
  simple bearer credential (e.g., OAuth2 client-credentials), only `TheDigitalCoreClient`'s
  internals change — `ITheDigitalCoreClient`'s contract and every caller are unaffected.
