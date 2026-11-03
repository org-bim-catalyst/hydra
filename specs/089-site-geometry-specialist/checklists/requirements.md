# Specification Quality Checklist: Site Geometry Specialist

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-10-25
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

**Status: all items pass. Ready for `/speckit-plan`.**

**Validation iteration 1 — notable decisions:**

- The specification deliberately names the **existing platform capabilities it must reuse** (the site
  analysis chain, the boundary machinery, the result-reporting channel) by reference to their specs, not by
  naming classes or files. That is a scope constraint on the feature, not an implementation instruction: the
  feature's value depends on the figures describing the same site the analysis was started for, and on the
  finding arriving through the same channel as every other specialist's. File-level routing belongs in
  plan.md.
- **OpenStreetMap is named** in FR-015 because the data source is user-visible provenance, not an internal
  detail: every finding states its data source (FR-022), and a user reading "OpenStreetMap" is reading a
  fact about their figure's trustworthiness. No class, SDK, or query shape is named.
- **Frontage was the one genuinely ambiguous figure.** The default recorded (per identifiable street, plus a
  total; unavailable when street adjacency cannot be established) is the only reading that satisfies the
  honesty requirement — approximating frontage from the longest edge would present an inferred figure as a
  measured one. Recorded as a clarification rather than left implicit.
- **The MapCapture capability was considered and rejected for this finding.** It can render a map frame, but
  this finding's scope is measurement, not imagery; the schematic-image specialist already owns imagery for
  an analysis. Recorded so a reviewer does not read its absence as an oversight.
- **Progressive arrival (SC-009) is now genuinely observable.** SPEC-057 could only verify its two-specialist
  promise with a throwaway test specialist; this feature is the first real second specialist, so the
  criterion is restated here as a production-observable behaviour.
- **No `[NEEDS CLARIFICATION]` markers were needed.** The brief settled the source, the figures, the
  no-image rule, the parcel-vs-site-boundary fallback, the shared-query-capability rule, and the confidence
  rule. The remaining choices had a single defensible answer each, and are recorded in the Clarifications
  section.

**Planning-time refinement (2026-10-26).** User Story 3 originally said a map-source outage means "no
geometry finding is delivered". That was wrong on the feature's own honesty model: area, dimensions and
orientation are computed from the boundary frozen on the analysis record and need no network, so the
correct behaviour is to deliver them and mark only the source-dependent figures (parcel outline,
frontage) unavailable with the reason — exactly the unavailable-figure pattern
`SiteEnvironmentalContextTool` already uses. US3, its acceptance scenarios, FR-025–FR-029 and SC-005 now
say that; a genuine fault (unreadable analysis, unexpected error) still fails the finding and is
recorded. No other requirement changed, and no checklist item regressed.
