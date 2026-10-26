# Implementation Plan: Environmental Context Specialist

**Branch**: `088-environmental-context-specialist` | **Date**: 2026-10-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/088-environmental-context-specialist/spec.md`

## Summary

Add a second specialist to the Site Analysis fan-out ([specs/057](../057-site-analysis-agent/spec.md)): flood
risk and climate/wind conditions for the resolved site, plus a typed reference to the platform's existing
client-side Solar Analysis ([specs/052](../052-solar-analysis/spec.md)). It follows the shipped specialist's
shape exactly — an `IAgentTool` receiving `{siteAnalysisId, siteName, siteLocation}`, composing panel content
through `SiteAnalysisContentComposer`, reporting only through `ISiteAnalysisResultRelay`.

The dominant design fact is that **no environmental data source exists**. `IFloodDataProvider` and
`IClimateDataProvider` were declared by 057 as the seam and deliberately shipped unimplemented. This feature
supplies their first, deliberately empty implementation — one that reports *unavailable* with a reason — so the
specialist is constructible and the absence of a source is stated rather than assumed. Every figure is rendered
as "No data source available" with its reason; the finding also names the environmental dimensions it does not
assess at all. No figure is estimated, inferred, or read off imagery.

Three supporting corrections ride with it, each verified against the code rather than assumed:

1. **The expected-result count must be derived, not hard-coded.** `RequestSiteAnalysisCapability` carries
   `private const int ExpectedResultCount = 1`. With a second branch provisioned, the relay settles the analysis
   as soon as the first specialist reports; the second report is then rejected because the analysis is no longer
   `Running` — the finding is silently lost. The count moves to a single shared declaration that also drives the
   provisioner's branch list (spec FR-003/FR-004).
2. **Sun and shadow are referenced, not recomputed.** The backend carries no astronomy and must not acquire one
   (052 research D3). The finding points at the viewer's Solar Analysis and says which site it describes.
3. **Confidence falls with unavailability.** A finding that measured nothing reports the lowest level, through
   the existing rule helper, from grounding facts only.

## Technical Context

**Language/Version**: C# / .NET 10 (backend). No frontend change: panel content reuses the existing block
vocabulary, and the client already treats the analysis type as an opaque string.

**Primary Dependencies**: MediatR (CQRS), FluentValidation, EF Core, Hangfire (existing workflow execution),
SignalR (existing push), Serilog, NUnit-style xUnit + NSubstitute for tests.

**Storage**: SQL Server via EF Core. No migration: `SiteAnalysisResult.AnalysisType` is already stored as a
string (`HasConversion<string>().HasMaxLength(40)`); `SiteAnalysisType.EnvironmentalContext` is 20 characters.

**Testing**: xUnit + NSubstitute (Application unit tests), mirroring `SiteAnalysisResultRelayTests` and
`SiteAnalysisProgressiveDeliveryTests`. No frontend tests: no frontend change.

**Target Platform**: ASP.NET Core web application; the specialist executes inside a workflow branch.

**Project Type**: Clean Architecture backend (`Domain`/`Application`/`Infrastructure`/`Persistence`/`Web`).

**Performance Goals**: None new. The specialist makes two provider calls with no network I/O while no source is
configured; it is bounded by the existing workflow/agent execution limits (spec FR-039/FR-040).

**Constraints**: Reports only via `ISiteAnalysisResultRelay`; content composes existing block kinds only
(`heading`, `text`, `keyValue`, `divider`); no document/image output; no new external service, credential, or
per-user configuration; no server-side solar computation.

**Scale/Scope**: One new tool class, one new analysis-type member, one new provisioner branch entry, two empty
provider implementations and their registrations, one shared specialist declaration, one composer method, one
relay display-name case, and their tests.

**Unknowns**: None outstanding. The one open product question — whether a real source is licensed — is
explicitly deferred by the specification and is out of scope.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| # | Principle | Assessment |
|---|---|---|
| §2 I | Clean Architecture / Dependency Rule | **PASS.** The tool and the empty providers are declared/implemented on the inward side of the boundary: the tool lives in Application and depends only on Application abstractions; the empty provider implementations live in Infrastructure, where a licensed source will also live. No outward reference is introduced. |
| §2 II | SOLID | **PASS.** The specialist is a new class, never an edit to the runtime (OCP). The provider seams gain an implementation, not a new contract. The shared specialist declaration removes a duplicated count rather than adding one. |
| §2 III | DRY / KISS / YAGNI | **PASS with one recorded tension.** The seam's empty implementation is the minimum needed for a consumer to exist; it invents no data and is replaced wholesale by a licensed source. The alternative — hard-coding "unavailable" in the tool — was rejected because it would make the seam dead code and force the future source to change the tool. |
| §2 IV | Composition over inheritance | **PASS.** No inheritance; the specialist composes the composer, the relay, and the provider seams. |
| §2 V | Dependency inversion & testability | **PASS.** Every collaborator is an injected abstraction; the tool is unit-testable with substituted providers, repository, composer, and relay. |
| §2 VI | Separation of concerns | **PASS.** Rendering rules live in the specialist; vocabulary knowledge stays in the composer; delivery and validation stay in the relay. |
| §2 VII | Convention over configuration | **PASS.** Tool name, node key, DI shape, and outcome vocabulary all mirror the shipped specialist and the existing seam. |
| §2 VIII | No Silent Failures (NON-NEGOTIABLE) | **PASS.** A provider *outcome* of unavailable/no-candidates/ambiguous is data and is rendered; a provider *exception* is a specialist failure, captured, persisted with its reason, and structured-logged, and the tool returns `AgentToolResult.Failure`. Nothing is swallowed. |
| §3 | Architecture rules (CQRS, repositories, DI) | **PASS.** Registration is the direct `services.AddScoped<IAgentTool, T>()` shape — never the `sp => sp.GetRequiredService<T>()` factory that deadlocked every tool resolution in 057 (docs/ARCHITECTURE.md). No CQRS surface changes. |
| §5 | Database | **PASS (no change).** No schema change; the analysis-type column is a string conversion. |
| §6 | API standards | **PASS.** No new endpoint; delivery reuses the existing hub and retrieval API. |
| §7 | UI principles | **PASS.** Existing panel and content components render the finding; no new component, block kind, or vocabulary version. |
| §8 | Security | **PASS.** No new credential, external call, or user input surface. Content is server-composed text from a typed provider result and is still validated by the relay. No provider URL or external address can reach the client. |
| §9 | AI principles | **PASS.** No AI model is called by this specialist; no prompt is introduced. Sun data is not re-derived server-side (052 D3). |
| §10 | Testing | **PASS.** Unit tests cover the unavailable path, the confirmed path, partial grounding, provider exceptions, coordinate sourcing, and the compositor's block kinds. A two-specialist progressive test becomes constructible for the first time because a second analysis type now exists. |
| §13 | Documentation | **PASS.** This plan set; 057's Follow-ups register already records the designation and the expected-count hazard. |

**Gate result: PASS.** The one tension (an empty implementation of a seam that shipped without one) is recorded
below.

**Post-Phase 1 re-check: PASS.** The design artifacts introduced no new violation: no new aggregate, no new
cross-boundary dependency, and no new public API.

## Project Structure

### Documentation (this feature)

```text
specs/088-environmental-context-specialist/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0 — verified decisions (D1–D11)
├── data-model.md        # Phase 1 — enum member, outcome rendering, content shape
├── quickstart.md        # Phase 1 — end-to-end validation guide
├── contracts/
│   ├── environmental-context-tool.md   # tool input/output, calls, failure, rendering
│   └── provider-seam.md                # the empty implementation contract + future licensed source
├── checklists/requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
src/
├── AskLucy.Domain/
│   └── SiteAnalysis/SiteAnalysisEnums.cs                    # MODIFIED — add EnvironmentalContext
│
├── AskLucy.Application/
│   ├── SiteAnalysis/
│   │   ├── SiteAnalysisSpecialists.cs                        # NEW — single source of truth for the fan-out
│   │   ├── SiteAnalysisContentComposer.cs                    # MODIFIED — multi-section composition
│   │   ├── SiteAnalysisResultRelay.cs                        # MODIFIED — display name for the new type
│   │   └── Tools/SiteEnvironmentalContextTool.cs             # NEW — the specialist
│   ├── Conversations/Capabilities/RequestSiteAnalysisCapability.cs  # MODIFIED — derived expected count
│   └── DependencyInjection.cs                                # MODIFIED — tool registration
│
├── AskLucy.Infrastructure/
│   ├── SiteAnalysis/Providers/                               # NEW
│   │   ├── UnavailableFloodDataProvider.cs
│   │   └── UnavailableClimateDataProvider.cs
│   ├── SiteAnalysis/SystemWorkflowProvisioner.cs             # MODIFIED — branch list from the shared declaration
│   └── DependencyInjection.cs                                # MODIFIED — provider registrations
│
└── (no persistence, web, or client change)

tests/
└── AskLucy.Application.Tests/SiteAnalysis/
    ├── EnvironmentalContextToolTests.cs                      # NEW
    └── SiteAnalysisProgressiveDeliveryTests.cs               # MODIFIED — real two-specialist case
```

**Structure Decision**: The existing Clean Architecture layout and feature folders are reused unchanged. The
specialist sits beside the shipped one in `Application/SiteAnalysis/Tools`; the empty providers sit where a
licensed implementation will sit (`Infrastructure/SiteAnalysis/Providers`). No new project, folder, block kind,
endpoint, or client module is introduced.

## Key Implementation Notes

Traps an implementer who has not read research.md will hit:

1. **Never hard-code the specialist count.** `RequestSiteAnalysisCapability`'s `ExpectedResultCount = 1` must be
   replaced by `SiteAnalysisSpecialists.All.Count`. Leaving it produces a silently rejected second finding — the
   relay validates `Status == Running`, and the analysis closes after the first report.
2. **The provisioner's branch list and the count must come from one declaration.** They live in different
   assemblies (Application cannot reference Infrastructure), so the declaration must live in Application and the
   provisioner must consume it. Two hand-maintained lists will drift.
3. **Read the site point from the analysis record, not from the tool input.** The providers take coordinates; the
   tool input carries `siteName`, never coordinates. Read `Latitude`/`Longitude` from the persisted
   `SiteAnalysis` in a fresh DI scope — the branch's scoped `DbContext` is shared with concurrent branches and is
   not safe to reuse (the shipped tool's own scope discipline).
4. **`unavailable` is not a failure.** Only an exception is. Conflating them either loses honest findings or
   hides a real fault behind "no data source".
5. **Do not compute any solar figure.** The backend has no astronomy dependency and must not gain one (052 D3).
   The finding contains a reference, not a number.
6. **Confidence never comes from `GeocodingCandidate.Importance`.** Use `SiteAnalysisConfidence.ResolveConfidence`
   with grounding facts: all figures grounded → High, partial → Medium, none → Low (057 rule 6).
7. **One image per finding does not apply here — there is no image at all.** Content is heading/text/keyValue/
   divider only; the composer must not emit an `image` block for this specialist.
8. **Do not register the tool through a `sp => sp.GetRequiredService<T>()` factory.** That shape deadlocked
   every `IEnumerable<IAgentTool>` resolution in 057 (docs/ARCHITECTURE.md) and is invisible to startup
   validation.
9. **No migration is needed.** Adding a string-converted enum member changes no schema; a migration generated
   "just in case" is noise and must not be added.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| The two provider seams acquire their first implementation in this feature, after 057 deliberately shipped them unimplemented (§2 III YAGNI) | A consumer now exists. Without an implementation the specialist cannot be constructed, and an unresolvable `IAgentTool` breaks every request that enumerates the tool catalog. The implementation is deliberately empty: its only behaviour is to report *unavailable* with a reason, which is exactly what the specification requires be said. | Hard-coding "no data source available" inside the specialist would make the seam dead code and force the licensed source to change the specialist — the opposite of the extensibility 057 committed to. |
| The expected-result count moves from a constant to a shared fan-out declaration (§2 III) | An analysis must know how many specialists it dispatched, and that number must equal the provisioned branch count. Two hand-maintained values drift, and drift here silently discards a finding. | Bumping `ExpectedResultCount` to `2` leaves two places to edit for a third specialist and preserves the defect. Deriving the count at dispatch time from the published workflow would add a repository read and couple the capability to the graph's shape. |

**No ADR required.** Neither entry introduces a datastore, a cross-cutting pattern, or an expensive-to-reverse
decision (§17). The first restores a seam to its intended use; the second removes a duplicated value.

## Follow-ups

- **A licensed flood/climate source** replaces the empty implementations; it is a vendor, licence, and credential
  decision, not part of this feature. The seam's outcome type carries no source identifier today, so a licensed
  implementation will need one to satisfy FR-030 truthfully (research D2).
- **The remaining specialists** — Urban Context, Connectivity & Access, Character Analysis — remain unstarted;
  the map-capture endpoint is designated as the imagery source for the last two.
