# Research: Proximity Analysis

## 1. Spatial request and agent integration

- **Decision**: Extend the existing Spatial Request Bundle and current authorized spatial/agent execution path; do not create a parallel orchestration path. The exact production type names and endpoint are not yet located by targeted searches.
- **Rationale**: The specification requires reuse and tool authorization. Existing repository evidence confirms site-analysis agent orchestration and result delivery, but not a literal Spatial Request Bundle symbol.
- **Alternatives considered**: Create a new standalone proximity endpoint/service independent of the bundle — rejected because it duplicates routing and authorization concerns. Guess type names from documentation — rejected as unsafe for downstream implementation.

## 2. Distance semantics and geometry

- **Decision**: Normalize supported distance input to meters; calculate using geodesic distance or an appropriate local projected coordinate system chosen by the existing spatial engine. Define `within` as distance <= threshold and `beyond` as distance > threshold. Distance bands are lower-inclusive and upper-exclusive except the final upper boundary, avoiding overlap.
- **Rationale**: A single explicit boundary convention makes deterministic, testable results; local projection/geodesic calculation avoids treating degrees as meters.
- **Alternatives considered**: Unprojected planar distance in longitude/latitude — rejected as geographically inaccurate. Arbitrary inclusivity — rejected due to boundary ambiguity.

## 3. Data sources and coverage

- **Decision**: Reuse configured spatial providers and their available feature geometries. Include data source/coverage metadata and mark partial results; do not imply complete coverage for absent or incomplete sources.
- **Rationale**: Provider choice, licensing, and completeness are already managed by existing data-source configuration; the requirement emphasizes provenance and transparent limitations.
- **Alternatives considered**: Add a new provider or persist a duplicate feature catalog — rejected pending concrete coverage need and a separate architecture decision.

## 4. Result and map-layer lifecycle

- **Decision**: Return a structured analysis result with resolved request, feature geometries/attributes, counts, provenance, coverage, and warnings. Use existing viewer layer lifecycle for buffers, selected results, and distance bands; keep layer state session-scoped in v1.
- **Rationale**: Meets Lucy interpretation and map-management requirements without introducing analysis-history persistence.
- **Alternatives considered**: Persist analysis jobs/results and rehydrate across sessions — excluded by the specification's session-only assumption. Encode all output as chat prose — rejected because the map requires structured spatial data.

## 5. Failure and cancellation behavior

- **Decision**: Validation failures are actionable user-visible responses; provider or processing failures use the platform's standard failure contract. If partial results are permitted by existing provider behavior, explicitly label them and include coverage warnings. Propagate cancellation and never silently discard failures.
- **Rationale**: Consistent with platform-wide no-silent-failure, Problem Details, and cancellation requirements.
- **Alternatives considered**: Return empty success on provider failure — rejected because it would misrepresent unavailable data as no matching features.

## 6. Scope and performance validation

- **Decision**: Validate performance against the specification's agreed-normal-area target (95% within 10 seconds), with explicit maximum area/result limits sourced from existing spatial request/provider constraints rather than new arbitrary constants.
- **Rationale**: Provides measurable performance while respecting existing quotas and avoiding speculative configuration.
- **Alternatives considered**: Establish new provider limits before discovering existing limits — deferred to implementation discovery; retain an unresolved research action below.

## Repository discovery actions before implementation

- Find and document the canonical Spatial Request Bundle contract, its agent tool registration, endpoint, validation/authorization path, and provider limit definitions. Current searches found the existing site-analysis agent (spec 057) and frontend site-analysis API, but did not identify the bundle type by literal-name search.
- Confirm the existing spatial calculation library/provider and geometry representation before selecting concrete adapter changes. Do not add a dependency unless existing capability is insufficient and an ADR justifies it.
