# Contract: Ask Lucy → TheDigitalCore Integration API

**Feature**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) (Decisions 3, 8, 12)

This is a **consumer-driven contract**: it documents what Ask Lucy's backend (`ITheDigitalCoreClient`,
`Infrastructure/TheDigitalCore/TheDigitalCoreClient.cs`) needs TheDigitalCore's API to provide. The
actual endpoint implementation lives in the `TheDigitalCore` repository, not this one — this file is
the handshake this feature's design assumes, to be confirmed with TheDigitalCore's own maintainers
before implementation.

## Authentication

- Every call below is authenticated as a single, dedicated Ask Lucy **service account**
  (Clarifications Q2, FR-026/FR-027) — never an individual Ask Lucy end user's own credential, because
  no Ask Lucy user has (or needs) a TheDigitalCore account.
- The credential (an API key or OAuth2 client-credentials grant — exact mechanism is TheDigitalCore's
  choice, `FLUMERIA-STUDIO-INTEGRATION-ARCHITECTURE.md` §7.4's still-open question, resolved by this
  feature to "some service-account credential, mechanism TBD with TheDigitalCore") is held server-side
  in Ask Lucy's `TheDigitalCoreIntegrationOptions` (`IOptions<T>`, secret-manager-backed per
  constitution §4), never in a browser bundle, never per-user.
- Least privilege: the service account should be scoped to Project search/create and this feature's
  result-attachment operation only — not general TheDigitalCore administrative access. Enforcing that
  scope is TheDigitalCore's responsibility (its own RBAC/service-account model), not something Ask
  Lucy can verify from its side; recorded here as an explicit assumption for TheDigitalCore's
  reviewers.

## Operations Ask Lucy needs

### 1. Search Projects by name

`GET /api/projects?siteName={text}` (path/shape illustrative — TheDigitalCore's actual routing
convention governs the real contract)

- **Purpose**: research.md Decision 8's primary match signal (Clarifications Q3).
- **Request**: the site name/description the user gave in chat.
- **Response**: zero, one, or many candidate Projects, each with at minimum: `projectId` (opaque
  string), `name`, `latitude`/`longitude` (if known), `companyId`.
- **Ask Lucy's handling**: zero results → proceed to coordinate search (below) or offer to create
  (FR-001e); exactly one high-confidence result → link automatically (FR-001d); multiple/ambiguous
  results → surface candidates to the user for confirmation (edge case added during
  `/speckit.clarify`), never pick one silently.

### 2. Search Projects by location

`GET /api/projects?near={lat},{lng}&radiusMeters={n}` (illustrative)

- **Purpose**: research.md Decision 8's secondary match signal, used when the name search is
  inconclusive.
- **Response**: same shape as above.

### 3. Create a Project

`POST /api/projects` (illustrative)

- **Purpose**: FR-001f — only ever called after explicit user confirmation in chat; never automatic.
- **Request**: at minimum `name` (the site name), `latitude`/`longitude` (from `resolve_site_boundary`),
  and whatever Company/ProjectType TheDigitalCore requires as mandatory fields for a new Project
  (per `TheDigitalCore/PROJECT-STATUS.md`'s existing Projects phase: Name, Number, Description,
  Company FK, ProjectType FK) — this feature supplies the fields it has (name, coordinates) and
  expects TheDigitalCore to apply its own required-field defaults/prompts for anything else, exactly
  as it would for a Project created through its own UI.
- **Response**: the new `projectId`.
- **Idempotency note**: Ask Lucy MUST search (operations 1-2) immediately before ever calling this,
  and only within the same user-confirmed turn (FR-001c precedes FR-001f) — this contract does not
  ask TheDigitalCore to deduplicate on its side; avoiding duplicate Projects is this feature's own
  responsibility per FR-001c/FR-001d.

### 4. Attach a Category Score Result to a Project

`POST /api/projects/{projectId}/site-analyses` (illustrative)

- **Purpose**: FR-026 — relay a completed Category Analysis Run's result for persistence as
  TheDigitalCore's own `SiteAnalysis`/`DesignRecommendation` record (FR-025: TheDigitalCore is the
  system of record, not Ask Lucy).
- **Request**: the Category Score Result shape defined in
  [site-analysis-category-result.md](./site-analysis-category-result.md) — category, score, findings
  (each with its triggering metric and supporting citation), any Data Gap Indications, and a
  reference back to the originating Ask Lucy `AgentExecution` id (for cross-system traceability, not
  for TheDigitalCore to resolve further).
- **Response**: success/failure. On failure, Ask Lucy retries per its existing HTTP-client retry
  convention; on final failure, the result is surfaced as a visible, actionable chat/UI error
  (SC-007) — never silently dropped.

## Error handling

Every operation above, on any non-success response, MUST be treated by Ask Lucy as a data-gap/
failure to surface, never a silent no-op — consistent with constitution §2.VIII and this spec's own
guardrails (FR-016, FR-018, SC-007). Ask Lucy does not assume a specific error envelope shape from
TheDigitalCore beyond "a non-2xx response is a failure"; mapping TheDigitalCore's actual error
responses to a user-visible message is an implementation-phase detail once TheDigitalCore's real API
contract is confirmed.

## Open item for TheDigitalCore's maintainers

This contract's exact routes/payload shapes are illustrative, written from Ask Lucy's side only.
Before implementation begins, this file should be reconciled with TheDigitalCore's actual
`ProjectsApiController` (and a new site-analysis-results endpoint, which does not exist yet per
`TheDigitalCore/PROJECT-STATUS.md`'s "Finished phases" list) and the chosen service-account
authentication mechanism (§7.4 of `FLUMERIA-STUDIO-INTEGRATION-ARCHITECTURE.md`).
