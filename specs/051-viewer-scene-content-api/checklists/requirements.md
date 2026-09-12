# Specification Quality Checklist: Viewer Scene and Content API

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

- This is the pivotal specification of the four. Four constraints it encodes were discovered by
  analysing the reference solar prototype against the existing map layer, and none is apparent from
  the code. They map to requirements as follows, and each exists because the alternative has already
  failed in this codebase or in the reference material:
  - **One viewer-owned reference point** (FR-008, FR-012, SC-002). The existing map layer has
    already placed content against a stale reference and had to re-anchor.
  - **One published coordinate convention** (FR-009, FR-010). The prototype's author lost time to
    rotation and floating bugs from an orientation mismatch; encoding it once prevents every future
    capability rediscovering it.
  - **Viewer-owned redraw** (FR-020 to FR-024, SC-006). Both the prototype and the existing map
    layer currently redraw continuously, which the prototype's own notes record as desynchronising
    the camera.
  - **Declared, not mutated, drawing settings** (FR-016 to FR-018, SC-003, SC-009). Drawing settings
    are global; a capability switching one on changes every other capability's appearance.
- Two decisions are recorded in the Clarifications section rather than left open: the chat page's
  viewer controls do not move here, and the colour/lighting change happens here rather than in
  specs/052.
- **FR-018 and SC-009 describe a deliberate visual regression event.** They are written as a review
  obligation with a recorded before-and-after per existing capability, not as a silent change. This
  is the highest-risk item in the feature and should be planned as its own task.
- Judgement calls recorded in Assumptions rather than escalated, each with a defensible default: a
  single 3D format initially; content served only through the platform's existing file access, never
  an arbitrary external address; element information read from the content rather than a separate
  platform store; and a single reference point being sufficient for one working area.
- **Split candidate.** This is the largest of the four specifications. If planning shows it is too
  large, the clean division is content (FR-001 to FR-007, FR-027 to FR-032) from drawing (FR-008 to
  FR-026, FR-033 to FR-034): extensions need the drawing half, Lucy needs the content half, and the
  two share only the reference point.
- SC-002's "agreed tolerance" and SC-004's smoothness comparison need concrete figures during
  planning. They are deliberately left unquantified here because the right numbers depend on the
  coordinate conversion's accuracy limits and on baseline measurement, neither of which is known yet.
