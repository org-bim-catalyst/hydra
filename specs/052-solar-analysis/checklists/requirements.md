# Specification Quality Checklist: Solar Analysis

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-12
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

- Three decisions are recorded in the Clarifications section rather than left open: site-local time
  rather than the reference implementation's UTC; visual and qualitative results only, with
  quantitative analysis deferred to its own specification; and building data retrieved through the
  platform rather than from the browser.
- **SC-009 is the real test of this feature.** Solar analysis is specified to require no change to
  the viewer, extension or panel frameworks. If it turns out to need one, that is evidence an
  earlier specification was incomplete, and the change belongs there rather than being absorbed
  quietly here. The Out of Scope section states this explicitly.
- **FR-043 and FR-044 exist for a reason beyond completeness.** Engineering software that makes
  solar claims should not be implicitly authoritative. A user must be able to discover that this is
  a design-stage study, what the calculation's accuracy is, and whether the building heights they
  are looking at were known or assumed. These are product requirements, not documentation ones.
- The framework conformance requirements (FR-037 to FR-042) restate obligations the framework
  already imposes. They are stated here because the reference implementation violates several of
  them — it creates its own drawing surface, sets its own reference point, drives its own redraw
  loop, and does not release what it draws. Anyone working from that prototype needs the divergence
  written down.
- Two accuracy figures are deliberately left to planning: SC-001's tolerance against an independent
  reference, and SC-002's shadow verification tolerance. Both should be inherited from the published
  algorithm's documented accuracy rather than invented.
- The accessibility assumption deserves review. A continuously-updating spatial display has no
  meaningful non-visual equivalent; the position taken here is that the controls and the numeric
  figures carry the information, and the display is an enhancement. If that is not acceptable, the
  figures panel's role grows.
- Quantitative analysis is the most likely thing to be asked for next and the most likely to be
  mistaken for a small addition. It is not: sunlight-hours and compliance assessment bring
  validation and liability obligations that a visual study does not.
