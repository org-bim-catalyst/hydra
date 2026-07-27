<!--
Sync Impact Report
Version change: [TEMPLATE] → 1.0.0 (initial ratification)
Modified principles: N/A (first concrete adoption of the constitution; template placeholders replaced)
Added sections:
  - Vision
  - Core Principles (I–VII)
  - Architecture Rules
  - Coding Standards
  - Database Principles
  - API Standards
  - UI Principles
  - Security
  - AI Principles
  - Testing Standards
  - Git Workflow
  - CI/CD
  - Documentation
  - Observability
  - Performance
  - Quality Gates
  - Decision Making
  - AI Coding Agent Rules
  - Definition of Done
  - Governance (amendments, versioning, compliance review)
Removed sections: none (template placeholders only)
Templates requiring updates:
  - .specify/templates/plan-template.md — ✅ no changes needed (Constitution Check section is generic and reads gates from this file at runtime)
  - .specify/templates/spec-template.md — ✅ no changes needed (technology-agnostic by design)
  - .specify/templates/tasks-template.md — ✅ no changes needed (task categories already accommodate testing/observability/security phases)
  - .claude/skills/speckit-*/SKILL.md — ✅ reviewed, generic references to "the constitution" only, no agent-specific or stale naming found
Follow-up TODOs: none — all placeholders resolved from project context supplied by the user.
-->

# Ask Lucy Constitution

Ask Lucy is an enterprise AI Workspace. This constitution is the highest engineering
authority in this repository. Every specification, architecture decision record,
implementation, pull request, and code review MUST comply with it. Where this document
and any other guidance conflict, this document governs.

## 1. Vision

**Purpose.** Ask Lucy gives enterprises a single, governed workspace for working with AI:
multi-provider chat, durable memory, retrieval over private knowledge, autonomous agents,
and integration with the tools an organization already runs on — beginning with Autodesk
BIM workflows — without ceding data custody or auditability to a consumer AI Workspace.

**Mission.** Make enterprise AI adoption safe, observable, and provider-neutral, so that
every user interaction — a chat message, a retrieved document, an agent action, a token
spent — is secured, attributable, and explainable after the fact.

**Long-term goals.**
- Become the system of record for enterprise AI interaction history, memory, and
  knowledge — durable, searchable, and exportable.
- Support any capable LLM provider or model without rearchitecting, so the platform
  never locks a customer into a single vendor's roadmap or pricing.
- Grow from individual productivity into team collaboration and cross-application
  integration (Autodesk BIM and adjacent enterprise systems) without a rewrite.
- Sustain a codebase that a new contributor — human or AI agent — can safely modify
  after reading this constitution and the relevant specification, without tribal
  knowledge.

**Core values.**
- **Trust over velocity.** A feature that is fast to ship but unauditable, insecure, or
  architecturally unsound is not done.
- **Security and data custody are deliverables**, not hardening passes performed later.
- **Provider neutrality.** No feature may assume a specific LLM vendor is permanent.
- **Architectural integrity compounds.** Decisions are made so the codebase is still
  comprehensible in year five, not just at next release.
- **Transparency.** Cost, token usage, model choice, and retrieval provenance MUST be
  visible to operators, not buried in logs no one reads.
- **Pragmatism.** Simplicity and YAGNI are respected — this constitution mandates
  discipline, not ceremony.

## 2. Core Principles

### I. Clean Architecture & the Dependency Rule (NON-NEGOTIABLE)

Source code is organized into four concentric layers — **Domain**, **Application**,
**Infrastructure**, **Presentation/API** — and dependencies MUST point inward only.
Domain MUST NOT reference Application, Infrastructure, or Presentation. Application MUST
depend only on Domain and on abstractions it defines itself. Infrastructure and
Presentation MAY depend on Application and Domain, never the reverse. Any dependency
arrow pointing outward-to-inward is a constitution violation and MUST be fixed before
merge, not suppressed or justified after the fact.

**Rationale:** this is the single rule that keeps a multi-year, multi-contributor,
multi-agent codebase refactorable. Every other architecture rule in this constitution
exists to enforce it.

### II. SOLID Design

Every class and interface MUST have a single, nameable reason to change (SRP). New
behavior MUST be added via new types/strategies rather than editing closed, tested logic
where a seam already exists (OCP). Subtypes MUST be substitutable for their base types
without surprising callers (LSP). Interfaces MUST be client-specific and narrow, not
fat "god interfaces" (ISP). High-level modules MUST depend on abstractions they own, not
on concrete low-level implementations (DIP — expanded in Principle V).

### III. Simplicity First — DRY, KISS, YAGNI

Duplication of **business logic** is forbidden — a rule MUST be expressed once and
referenced, not copy-pasted (DRY). Duplication of superficially similar but
conceptually unrelated code is acceptable; premature abstraction to avoid three similar
lines is not (see the DRY/coupling trade-off below). Solutions MUST use the simplest
design that satisfies the current, specified requirement (KISS). Code, tables, flags, or
abstractions MUST NOT be built for hypothetical future requirements not present in an
approved specification (YAGNI). When DRY and simplicity conflict, prefer the simpler,
more duplicated code until a third real use case proves the abstraction is warranted.

### IV. Composition Over Inheritance

Behavior is shared via composition, delegation, and small interfaces MUST be preferred
over deep class inheritance hierarchies. Inheritance MAY be used only to model a genuine
is-a relationship with stable, shared invariants (e.g., a small `Entity<TId>` base for
identity semantics). Inheritance chains deeper than two levels in application or domain
code require explicit justification in the PR description.

### V. Dependency Inversion & Testability

Application and Domain layers MUST depend on interfaces they define (e.g.,
`ILlmProvider`, `IChatRepository`, `IUnitOfWork`), never on concrete Infrastructure
types. Every Application service MUST be unit-testable with all dependencies mocked or
faked, with no database, network, or file-system access required. Constructors MUST
receive dependencies via constructor injection; service location and static singletons
are forbidden outside the Composition Root.

### VI. Separation of Concerns

Presentation (controllers, React components) MUST NOT contain business logic —
validation rules, pricing, retrieval ranking, or provider selection belong in
Application/Domain. Infrastructure concerns (SQL, HTTP clients, file I/O, SMTP, PayPal
SDK calls) MUST NOT leak into Domain or Application beyond the interfaces those layers
define. Cross-cutting concerns (logging, caching, transactions, authorization) are
implemented as decorators/behaviors/middleware, not inlined into every handler.

### VII. Convention Over Configuration

Where a strong, documented project convention exists (folder layout, MediatR pipeline
registration, naming, EF Core configuration discovery), new code MUST follow it rather
than introducing a parallel bespoke mechanism. Configuration/flexibility is reserved for
values that genuinely vary by environment or tenant (connection strings, provider API
keys, feature flags) — not for structural decisions this constitution already settles.

## 3. Architecture Rules

**Solution shape.** The backend solution MUST be organized as (at minimum)
`Domain`, `Application`, `Infrastructure`, and `Api` projects, with additional
`Infrastructure.*` satellite projects permitted for genuinely separable concerns
(e.g., `Infrastructure.Ai`, `Infrastructure.Persistence`). The frontend is a separate
`Frontend` (or `web`) project and communicates with the backend only through its public
HTTP API — it MUST NOT reference backend assemblies or reach the database directly.

**Allowed dependencies:**
- `Domain` → nothing (no project references, no NuGet packages beyond primitives such
  as base class libraries).
- `Application` → `Domain` only, plus cross-cutting abstractions it defines (MediatR,
  FluentValidation *interfaces/usage*, not infrastructure clients).
- `Infrastructure.*` → `Application`, `Domain`.
- `Api` → `Application`, `Infrastructure.*` (for Composition Root registration only),
  `Domain` (transitively).

**Forbidden dependencies:**
- `Domain` → `Application`, `Infrastructure`, `Api`, EF Core, ASP.NET Core, any SDK.
- `Application` → `Infrastructure`, `Api`, EF Core, HttpClient, any concrete provider SDK
  (OpenAI/Anthropic/etc. clients), Serilog sinks.
- Any layer → circular references between sibling `Infrastructure.*` projects.
- Controllers/endpoints calling `DbContext`, repositories, or provider SDKs directly,
  bypassing MediatR/Application services.

**Domain purity.** Domain entities, value objects, domain events, and domain services
MUST contain no attributes or references tied to EF Core, ASP.NET, JSON serialization
libraries, or any I/O concern. Persistence mapping (keys, indexes, shadow properties)
lives entirely in `Infrastructure` via EF Core Fluent API configuration classes, never
as attributes on Domain classes.

**CQRS rules.** All application behavior is expressed as MediatR `IRequest` commands
(state-changing) or queries (read-only), each with exactly one handler. Commands MUST
NOT return full read models beyond what the caller needs to confirm the write (an id,
a result DTO) — they MUST NOT be used to fetch unrelated data. Queries MUST NOT mutate
state. Cross-cutting behavior (validation, logging, transactions, authorization) is
implemented as `IPipelineBehavior<TRequest, TResponse>`, not duplicated per handler.

**Domain events.** State transitions that other parts of the system must react to
(e.g., "ChatArchived", "SubscriptionRenewed") MUST be raised as domain events from the
aggregate and dispatched after a successful commit, never invoked as direct in-process
side effects from within a handler. Domain events MUST NOT be used as a substitute for
a return value in the same request/response cycle.

**Application services.** Application services/handlers orchestrate: validate input
(FluentValidation), invoke Domain logic, coordinate repositories/Unit of Work, map to
DTOs. They MUST NOT contain business rules that belong on the Domain model — "anemic
domain calling into a fat service" is a violation of Principle I and VI.

**Repository & Unit of Work rules.** Repositories are defined as interfaces in
`Application` (or `Domain` for aggregate-root repositories) and implemented in
`Infrastructure`. Repositories MUST expose aggregate-oriented methods, not a generic
leaky `IQueryable` escape hatch used to build ad-hoc queries in Application code —
read-heavy, cross-aggregate queries belong in dedicated query handlers using EF Core
directly inside `Infrastructure`. All writes within one request MUST commit through a
single `IUnitOfWork` transaction boundary.

**Infrastructure isolation.** Every external dependency — SQL Server, LLM provider
SDKs, file storage, SMTP, PayPal — MUST be hidden behind an interface owned by
`Application`/`Domain`. Swapping an LLM provider or storage backend MUST be achievable
by adding a new `Infrastructure` implementation and a DI registration change only, with
zero changes to `Application` or `Domain`.

**Dependency Injection.** All service registration happens in the Composition Root
(`Api` startup / DI extension methods per project, e.g. `AddApplication()`,
`AddInfrastructure()`). Lifetimes MUST be explicit and correct: `DbContext` and
per-request state are scoped; stateless services are singleton; anything holding
per-operation state is transient. `new`-ing up a concrete Infrastructure dependency
inside Application/Domain code is forbidden.

## 4. Coding Standards

**C#.** Nullable reference types MUST be enabled solution-wide (`<Nullable>enable</Nullable>`);
a member is either non-nullable and guaranteed, or explicitly `?` and checked. `async`/`await`
MUST be used for all I/O; `async void` is forbidden except for framework event handlers;
`.Result`/`.Wait()`/`.GetAwaiter().GetResult()` on a Task are forbidden outside of
composition-root bootstrap code. `CancellationToken` MUST be accepted and propagated
through every async Application/Infrastructure method that performs I/O.

**TypeScript.** `strict` mode MUST be enabled; `any` is forbidden except at the
narrowest possible boundary with a `// TODO` justification comment reviewed in PR. Props
and API payloads MUST be typed from a single source of truth (generated or hand-written
types shared with backend contracts), not re-declared ad hoc per component.

**Naming.** C#: PascalCase for types/methods/public members, camelCase for locals/
parameters, `IPascalCase` for interfaces, `_camelCase` for private fields. TypeScript:
PascalCase for components/types, camelCase for variables/functions, `useX` for hooks.
Names MUST describe domain intent (`RefreshTokenRotationPolicy`) over implementation
detail (`Helper`, `Manager`, `Util` are forbidden as standalone type name suffixes
without a qualifying domain noun).

**Folder structure.** Backend projects are organized by layer first, then by feature/
aggregate within `Application` and `Infrastructure` (e.g.,
`Application/Chats/Commands/SendMessage`). Frontend is organized by feature-domain under
`src/features/<domain>`, with cross-feature primitives under `src/shared`.

**Documentation & comments.** Public API surfaces (controllers, MediatR requests,
exported TS types/hooks) MUST carry a one-line XML doc / TSDoc summary describing intent,
not mechanics. Inline comments are written only when the code cannot explain itself —
a non-obvious constraint, a workaround for a specific defect, a subtle invariant. Comments
that restate what the next line does are forbidden and MUST be removed in review.

**Error handling.** Domain invariant violations throw dedicated domain exceptions
(e.g., `DomainRuleViolationException`), never generic `Exception`. Application handlers
MUST NOT swallow exceptions; a global exception-handling middleware translates
exceptions to RFC 7807 Problem Details at the API boundary (see §6). Frontend data
fetching MUST surface errors through TanStack Query's error state, not silent
console-only failures.

**Logging.** All logging goes through Serilog structured logging (`ILogger<T>`) with
named properties, never string-concatenated messages. Secrets, tokens, and raw prompt/
PII content MUST NOT be logged at any level above `Debug`, and MUST NOT be logged at all
in production sinks.

**Configuration.** Configuration is bound to strongly-typed `IOptions<T>` classes,
validated at startup (`ValidateOnStart`); reading `IConfiguration["Key"]` by raw string
outside of the binding layer is forbidden. Secrets MUST come from environment variables,
user-secrets (dev), or a secret manager (Azure Key Vault in production) — never
committed to source control.

**Magic values, constants, enums, records, value objects.** Magic strings/numbers with
domain meaning MUST be named constants or enums, not repeated literals. C# `enum` is used
for closed, stable sets; when behavior varies per case, prefer a smart enum / discriminated
pattern over `switch` sprawl. Immutable domain concepts with structural equality (Money,
EmailAddress, ModelIdentifier) MUST be modeled as `record`/value objects, not primitive
strings/decimals passed around ("primitive obsession" is a review-blocking finding).

## 5. Database Principles

**Entity design & keys.** Every entity has a surrogate key (`Guid` v7/sequential or
`long` identity — never a natural key as primary key). Aggregate roots own their child
entities' lifecycle; child entities MUST NOT be reachable via their own `DbSet` outside
the aggregate's repository. Foreign keys are always indexed.

**Indexes.** Every column used in a `WHERE`, `JOIN`, or `ORDER BY` on a query path MUST
be covered by an index; indexes are added in the same migration as the query that needs
them and justified in the PR description if composite.

**Migrations.** All schema changes go through EF Core migrations, committed to source
control, one logical change per migration. Migrations MUST be reversible (a working
`Down`) or explicitly documented as irreversible with a stated reason. Destructive
migrations (drop column/table) require a two-step deploy (stop reading → stop writing →
drop) documented in the PR when the column has been in production.

**Concurrency.** Entities that can be updated concurrently by more than one actor MUST
carry a concurrency token (`rowversion`/`xmin`-equivalent); `DbUpdateConcurrencyException`
MUST be handled explicitly at the Application layer, not left to bubble as a 500.

**Soft deletes & auditing.** User-facing records with retention/compliance requirements
(chats, knowledge base documents, billing records) use soft delete (`IsDeleted`,
`DeletedAtUtc`) enforced via a global EF Core query filter; hard delete is reserved for
GDPR-style erasure requests via an explicit, audited command. All entities MUST carry
`CreatedAtUtc`/`CreatedBy` and `LastModifiedAtUtc`/`LastModifiedBy`, populated by a
SaveChanges interceptor, never set manually by callers.

**Performance & transactions.** N+1 query patterns are forbidden — Application queries
MUST project into DTOs with explicit `Include`/`Select`, verified against generated SQL
for non-trivial queries. A business transaction spans exactly one `SaveChanges`/Unit of
Work commit; multi-step workflows use domain events or an outbox, not multiple partial
commits.

**RAG & vector storage.** Vector embeddings are stored in SQL Server using native vector
column types/vector search, co-located with the source document metadata — no separate
vector database MAY be introduced without an ADR justifying why SQL Server vector search
is insufficient. Chunking strategy, embedding model identifier, and embedding version
MUST be stored alongside each vector so re-embedding on model upgrade is a data migration,
not a guess. Retrieval queries MUST be provider/model-tagged so retrieval quality can be
compared across embedding model versions.

## 6. API Standards

**REST conventions.** Resources are nouns, plural, kebab/lowercase (`/api/v1/chats/{id}/messages`);
verbs live in HTTP methods, not URLs. State-changing operations that don't map cleanly to
CRUD (e.g., `retry`, `archive`) are modeled as a sub-resource action
(`POST /chats/{id}/actions/retry`), not a query-string flag.

**Versioning.** The API is versioned in the URL path (`/api/v1/...`). A breaking change
to a shipped contract requires a new version segment; additive, backward-compatible
fields MAY ship into an existing version. A version MUST NOT be removed until all known
consumers have migrated and a deprecation window (documented in release notes) has
elapsed.

**Problem Details.** All error responses MUST conform to RFC 7807 Problem Details
(`application/problem+json`) with `type`, `title`, `status`, `detail`, and a
`traceId`/correlation id extension member. Ad hoc error shapes (`{ "error": "..." }`)
are forbidden.

**Pagination, filtering, sorting.** List endpoints MUST be paginated by default
(cursor-based for high-churn collections like chat messages, offset-based acceptable for
small stable admin lists) and MUST NOT return unbounded result sets. Filtering and
sorting are expressed via documented query parameters, validated server-side; free-form
query-string-to-SQL translation is forbidden.

**AuthN/AuthZ.** Authentication is via ASP.NET Identity-issued JWT access tokens plus
rotating refresh tokens; every endpoint is `[Authorize]` by default, with anonymous
access an explicit, reviewed opt-in. Authorization decisions (role, tenant, ownership)
are enforced in Application-layer authorization handlers, not scattered `if` checks in
controllers.

**Streaming.** Token-by-token AI responses are streamed to the client via Server-Sent
Events (or an equivalent chunked-transfer mechanism), never buffered fully server-side
before the first byte is sent, and MUST support client-initiated cancellation that
propagates to the underlying provider call.

**Rate limiting.** Every public endpoint is subject to rate limiting (per-user and
per-tenant); AI-invoking endpoints additionally enforce token/cost-based throttling, not
just request-count throttling.

**OpenAPI.** Every endpoint MUST be discoverable via the generated OpenAPI document with
accurate request/response schemas and documented status codes; undocumented "shadow"
endpoints are a merge-blocking finding.

## 7. UI Principles

The frontend is built with React 19, TypeScript, Vite, and Material UI (MUI) as the
component and theming foundation.

- **Design system.** New UI is composed from the existing MUI theme and shared component
  library before a bespoke component is written; a new shared component requires it be
  used by at least two features or justified as a foundational primitive.
- **Accessibility.** All interactive UI MUST meet WCAG 2.1 AA: keyboard operability,
  visible focus states, correct ARIA roles, and color contrast — verified via automated
  a11y checks in CI plus manual review for new interaction patterns (see §10).
- **Responsive design.** Layouts MUST work from mobile breakpoints through desktop using
  MUI's responsive breakpoint system; fixed-pixel layouts that break under resize are a
  review-blocking finding.
- **Theming.** Light and dark themes MUST both be supported through the MUI theme
  provider; components MUST NOT hardcode colors that bypass the theme.
- **State management.** Client/UI state (dialogs, drafts, transient UI flags) lives in
  Zustand stores; server state (anything fetched from the API) lives in TanStack Query
  and MUST NOT be duplicated into Zustand. Forms use React Hook Form with schema
  validation matching the backend's FluentValidation rules.
- **Performance.** Route-level code splitting is mandatory; long lists (chat history,
  message threads) MUST be virtualized; large dependencies are lazy-loaded behind the
  feature that needs them.
- **Internationalization.** User-facing strings MUST NOT be hardcoded inline once an
  i18n framework is introduced for a locale beyond the platform default; until then, all
  user-facing copy is centralized (not scattered as literals) so i18n extraction is
  mechanical when needed.

## 8. Security

Security is a delivered feature of every change, not a follow-up task.

- **OWASP Top 10** is the baseline threat model for every endpoint and UI surface handling
  user input, file uploads, or AI-generated content.
- **Secrets** never live in source control, client bundles, or logs; they are injected via
  environment/secret manager at runtime and rotated on a documented schedule.
- **Authentication** is ASP.NET Identity + JWT with short-lived access tokens and rotating,
  single-use refresh tokens; refresh token reuse detection MUST revoke the token family.
- **Authorization** is enforced server-side on every request, including AI agent and MCP
  tool invocations — a client-hidden button is not an authorization control.
- **Prompt injection.** Content retrieved from RAG, tool outputs, or third-party
  integrations (MCP servers) is treated as untrusted data, never as instructions; system
  prompts MUST structurally separate trusted instructions from untrusted retrieved/tool
  content, and agent tool-execution MUST be permission-scoped per tool.
- **XSS.** All user- and model-generated content rendered in the UI MUST be sanitized
  before rendering as HTML/Markdown; React's default escaping MUST NOT be bypassed
  (`dangerouslySetInnerHTML`) without a documented sanitizer.
- **CSRF.** State-changing requests from browser sessions MUST be protected (SameSite
  cookies plus anti-forgery tokens or bearer-token-only auth with no ambient cookie
  auth for APIs).
- **SQL injection.** All data access goes through EF Core parameterized queries; raw SQL
  (`FromSqlRaw`, `ExecuteSqlRaw`) MUST use parameters, never string interpolation of
  user input.
- **Rate limiting & encryption.** See §6 for rate limiting; data in transit is TLS-only,
  data at rest for secrets and sensitive PII uses column/field-level encryption in
  addition to disk-level encryption.
- **Audit logs.** Authentication events, authorization denials, billing changes, and AI
  agent tool executions MUST be written to an immutable audit trail distinct from
  general application logs.
- **File validation.** Uploaded files are validated by content (magic-byte sniffing), not
  by extension/MIME header alone, and scanned/size-limited before being persisted or fed
  into RAG ingestion.
- **Least privilege & secure defaults.** Service accounts, database users, and API keys
  are scoped to the minimum required permission; every new feature flag/config defaults
  to the more restrictive/secure option.

## 9. AI Principles

- **Provider abstraction.** All LLM access goes through an `ILlmProvider`-style
  abstraction in Application; no Application/Domain code references a specific vendor
  SDK. Adding a provider is an `Infrastructure.Ai` addition plus configuration, never a
  change to calling code.
- **Model abstraction.** Model selection (which provider, which model, which
  parameters) is configuration/policy-driven per request, not hardcoded per feature —
  a feature asks for capabilities (e.g., "long-context", "vision"), not a specific model
  string, wherever practical.
- **Prompt engineering.** System and tool prompts are versioned artifacts (not inline
  string literals scattered across handlers), reviewed like code, and testable in
  isolation from the model call.
- **RAG architecture.** Retrieval is a first-class pipeline stage (ingest → chunk →
  embed → store → retrieve → rerank → assemble context), each stage independently
  observable and independently swappable.
- **Memory.** AI memory (user/session-durable facts) is stored distinctly from chat
  history, with explicit user visibility and control (view/edit/delete), never inferred
  silently and used without a way for the user to audit it.
- **Agent architecture.** Agents operate over an explicit, scoped tool set; every tool
  invocation is authorized, logged, and bounded (timeouts, iteration limits) — an agent
  MUST NOT have implicit access to any capability beyond what its task grants.
- **Token usage & cost monitoring.** Every AI call records input/output token counts,
  model, and estimated cost against the initiating user/tenant, feeding both
  observability (§14) and billing.
- **Streaming.** User-facing generation is streamed by default (see §6); non-streaming
  is the exception, justified by the use case (e.g., background batch classification).
- **Fallback providers.** Features with an availability SLA MUST define a fallback
  provider/model policy for outage or rate-limit conditions, applied via the provider
  abstraction, not ad hoc `try/catch` per call site.
- **Observability.** Every AI call is traceable end-to-end (prompt version, retrieved
  context ids, model, latency, token cost) via the correlation id described in §14.

## 10. Testing Standards

- **Unit tests** cover Domain logic and Application handlers with all Infrastructure
  dependencies faked; they MUST run without a database, network, or filesystem.
- **Integration tests** cover Infrastructure implementations (EF Core repositories
  against a real/test SQL Server instance, provider adapters against recorded/replayed
  responses) and MUST run in CI, not only locally.
- **End-to-end tests** cover critical user journeys (sign-up, first chat, RAG query,
  checkout) through the real API and UI.
- **Accessibility tests** run automated a11y checks (e.g., axe) in CI for new/changed
  UI, supplementing manual review for novel interaction patterns.
- **Performance tests** exist for endpoints and queries with a stated performance goal
  (§15) and MUST fail the build on regression past the documented threshold.
- **Coverage expectations.** Domain and Application layers target ≥80% line coverage as
  a floor, not a target to game — a covered line with no meaningful assertion does not
  satisfy this constitution. Coverage percentage MUST NOT be used to block a PR that
  is otherwise correct and adequately tested; it is a signal, not a gate by itself.
- Tests are written for new behavior in the same PR that introduces it, never deferred
  to a follow-up ticket.

## 11. Git Workflow

- **Branching.** `master` is always releasable. Work happens on short-lived feature
  branches named `<###-feature-slug>` (matching the Spec Kit `specs/` numbering) or
  `fix/<slug>`, branched from and merged back to `master` via pull request.
- **Commit messages.** Commits use Conventional Commits style (`feat:`, `fix:`, `docs:`,
  `refactor:`, `test:`, `chore:`) with an imperative, present-tense summary line; body
  explains *why* when not obvious from the diff.
- **Pull requests.** Every PR links the driving specification/issue, states what changed
  and why, and is scoped to one coherent unit of work — unrelated cleanups travel in
  their own PR.
- **Code review.** At least one approval is required before merge; the reviewer verifies
  architecture compliance (§3), not just correctness. Author and reviewer are jointly
  accountable for constitution compliance.
- **Release tagging & versioning.** Releases are tagged using SemVer
  (`MAJOR.MINOR.PATCH`); breaking API contract changes bump MAJOR, additive features bump
  MINOR, fixes bump PATCH.

## 12. CI/CD

Every pull request MUST pass, in order, before merge is permitted:

1. **Restore & build** — backend and frontend build with zero errors and zero new
   compiler warnings on changed files.
2. **Lint & format** — backend analyzers/formatter and frontend ESLint/Prettier (or
   equivalent) pass with no violations on changed files.
3. **Tests** — unit and integration test suites (§10) pass; a flaky test is quarantined
   and tracked, never silently skipped.
4. **Security scanning** — dependency vulnerability scanning and secret scanning run on
   every PR; a detected secret blocks merge until rotated and removed from history.
5. **Artifact generation** — Docker images are built for backend and frontend on merge
   to `master`, tagged with the commit SHA and, on release, the SemVer tag.
6. **Deployment approval** — deployment to staging is automatic on `master`;
   deployment to production requires an explicit manual approval gate.

CI/CD is implemented via GitHub Actions; pipeline definitions live in version control
alongside the code they build and are reviewed with the same rigor as application code.

## 13. Documentation

- **Architecture Decision Records (ADRs).** Significant, hard-to-reverse architectural
  decisions (new datastore, new cross-cutting pattern, provider abstraction changes) are
  recorded as ADRs under `/docs/adr/`, numbered sequentially, capturing context, decision,
  and consequences.
- **Specifications.** Features are specified under `/specs/<###-feature>/` via the Spec
  Kit workflow (spec → plan → tasks) before implementation begins.
- **README.** Each deployable project (backend API, frontend app) carries a README
  covering purpose, local setup, and how to run its tests.
- **API documentation.** The OpenAPI document (§6) is the source of truth for API
  documentation; it MUST stay in sync with the implementation as a CI-checked artifact,
  not a manually maintained duplicate.
- **Release notes** are written per release, summarizing user-facing changes,
  deprecations, and migration steps.
- **Migration guides** accompany any breaking API version bump or destructive database
  migration reaching production.
- **Design documentation** for a feature (data model, sequence diagrams) lives with its
  spec under `/specs/<###-feature>/`.

## 14. Observability

- **Logging.** Serilog structured logging, with a minimum of Information level for
  business events and Warning+ for recoverable failures, shipped to a centralized sink.
- **Metrics.** Key business and system metrics (requests/sec, latency percentiles, token
  spend, queue depth) are emitted and dashboarded, not only discoverable via log search.
- **Tracing.** Distributed tracing (OpenTelemetry-compatible) spans a request from API
  entry through Infrastructure/provider calls, including AI provider round-trips.
- **Health checks.** Every service exposes a `/health` (liveness) and `/health/ready`
  (readiness, checking DB/provider connectivity) endpoint used by orchestration and CI/CD
  deployment gates.
- **Correlation IDs.** Every request is assigned a correlation id at the edge, propagated
  through logs, traces, and downstream provider calls, and returned to the client in
  error responses (§6) for support diagnosis.
- **Error reporting & performance monitoring.** Unhandled exceptions and performance
  regressions against §15 budgets are surfaced to an alerting channel, not just logged.

## 15. Performance

- **Frontend.** Route-based code splitting, virtualization for long lists, and a bundle-
  size budget per route are enforced; regressions beyond the budget fail CI or require an
  explicit, reviewed exception.
- **Backend.** All I/O is asynchronous; hot paths avoid unnecessary allocations and
  N+1 queries (§5); response caching is used for expensive, cacheable reads.
- **SQL optimization.** Query plans for new non-trivial queries are reviewed before
  merge; missing-index and full-scan patterns are treated as bugs.
- **Caching.** Distributed caching (e.g., for provider model metadata, frequently-read
  reference data) is used where correctness under staleness is acceptable and documented.
- **Streaming & lazy loading.** AI responses stream (§6/§9); large frontend dependencies
  and admin-only surfaces are lazy-loaded, not part of the initial bundle.
- **Scalability.** API instances are stateless (session/refresh-token state lives in the
  database/cache, not in-process) so the backend scales horizontally without sticky
  sessions.

## 16. Quality Gates

A change MUST NOT merge unless all of the following are true:

1. **Architecture compliance** — no Dependency Rule violations (§3); reviewer has
   explicitly checked this, not assumed it from passing tests.
2. **Automated testing** — required tests (§10) exist for new/changed behavior and pass
   in CI.
3. **Documentation updated** — spec, ADR, README, or API docs updated when the change
   affects them (§13).
4. **Accessibility review** — completed for user-facing UI changes (§7, §10).
5. **Performance review** — completed for changes touching a path with a stated
   performance goal (§15).
6. **Security review** — completed for changes touching auth, data access, file
   handling, or AI tool/agent capability (§8).

A gate MAY be marked not-applicable with a one-line justification in the PR; it MUST NOT
be silently skipped.

## 17. Decision Making

Architectural decisions are made by the engineer(s) closest to the problem, guided by
this constitution, and recorded — not by unilateral fiat and not by unrecorded
consensus. A decision requires an ADR (§13) when it: introduces a new datastore or
cross-cutting infrastructure dependency; changes a rule in this constitution's scope;
introduces a new architectural pattern not already established in the codebase; or
is expensive to reverse (data migration, public contract change). Every ADR MUST record
the alternatives considered and the trade-off accepted, not just the choice made. When
in doubt between a short-term-convenient option and a long-term-maintainable one, this
constitution's default is maintainability — deviating requires an ADR explaining why the
short-term option was chosen and what repayment plan, if any, exists.

## 18. AI Coding Agent Rules

AI coding agents (including Claude Code and any Spec Kit-driven agent) operating in this
repository MUST:

- Never violate Clean Architecture or the Dependency Rule (§3) to satisfy a task faster.
- Never duplicate business logic that already exists elsewhere in Domain/Application —
  find and reuse or extend it instead.
- Never invent requirements not present in the approved specification; ambiguity is
  resolved by asking the user or, where the workflow supports it, via `/speckit-clarify`,
  not by guessing.
- Always read the relevant specification, plan, and this constitution before
  implementing a feature-sized change.
- Always update or add tests when changing observable behavior (§10) — a behavior change
  without a corresponding test change is incomplete.
- Always update documentation (spec, ADR, README, API docs) when architecture or public
  contracts change (§13).
- Ask for clarification when requirements conflict with each other or with this
  constitution, rather than silently picking one interpretation.
- Explain architectural trade-offs before implementing a change that deviates from an
  established pattern or touches more than one architectural layer's public contract.
- Never weaken a security control (auth check, input validation, rate limit) to make a
  test pass or a feature "just work."
- Treat this constitution as binding on generated code exactly as it is on human-written
  code — no exception applies merely because the author is an agent.

## 19. Definition of Done

A feature is complete only when all of the following hold:

- All acceptance criteria in its specification are satisfied.
- Required tests (§10) exist and pass in CI.
- Documentation affected by the change is updated (§13).
- No unresolved architecture violations exist (§3, §16).
- Accessibility requirements are met for any user-facing UI (§7).
- Security review is complete for any change touching auth, data, files, or AI
  tool/agent capability (§8, §16).
- Performance is within the stated goals for any path with a performance budget (§15).
- Code review is complete with at least one approval (§11).

Partial, half-finished implementations MUST NOT be merged behind an unimplemented flag
"to unblock later" — a feature either ships complete per its spec or does not merge.

## Governance

This constitution supersedes any conflicting practice, template default, or prior
convention in this repository. Every specification, plan, task list, pull request, and
code review MUST be evaluated against it; the Constitution Check gate in the planning
workflow (`/speckit-plan`) exists specifically to enforce this.

**Amendment process.** Amendments are proposed via pull request against this file,
MUST include a completed Sync Impact Report (as prepended to this file's HTML comment
header) describing what changed and why, and MUST identify any dependent template or
documentation updates required as a result. An amendment requires the same architectural
review rigor as any other change to §3 (Architecture Rules) — a change to a principle is
not lower-stakes than a change to code that implements it. Justification MUST be
recorded in the amendment's PR description or an accompanying ADR (§13/§17) when the
change is more than a wording clarification.

**Versioning policy.** This constitution is versioned independently using semantic
versioning:
- **MAJOR** — backward-incompatible governance changes: a principle or article is
  removed or redefined in a way that invalidates prior compliant work.
- **MINOR** — a new principle, article, or materially expanded rule is added, or an
  existing rule is meaningfully expanded in scope.
- **PATCH** — clarifications, wording fixes, typo corrections, and non-semantic
  refinements that do not change what is required or forbidden.

**Compliance review.** Every PR review MUST include an explicit architecture/constitution
compliance check (§16); this is not optional even when the change appears small.
Complexity introduced against a principle in §2 MUST be justified in the PR description
or, for anything non-trivial, in an ADR — silent complexity is treated as a defect.
Stability is the default posture: this constitution changes deliberately and rarely, not
reactively per feature.

**Version**: 1.0.0 | **Ratified**: 2026-07-27 | **Last Amended**: 2026-07-27
