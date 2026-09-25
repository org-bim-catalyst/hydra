# Specification Quality Checklist: Studio HUD Top Row

**Purpose**: Validate specification completeness and quality before proceeding to planning

**Created**: 2026-09-25

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

- Validation passed on first iteration.
- Two judgement calls recorded in Assumptions, which are worth confirming in `/speckit-clarify` if the user disagrees: (1) the violet brand stripe stays and only the icon is coloured; (2) the per-level icon shapes stay (the Low level uses a question mark, not a shield).
- The "Also considered: …" alternative-candidates line is removed along with the source line, following the "keep only the first 2 lines" instruction.
- **Superseded by clarification**: the two judgement calls above were settled the other way in `/speckit-clarify`. The violet stripe is removed (all three cards share one theme-driven surface), and every level now uses a shield-family icon (`GppGoodOutlined` / `ShieldOutlined` / `GppMaybeOutlined`).
- **Implementation (2026-09-25)**: T001–T037 are done and the automated gate is green (tsc, eslint, and vitest 1839/1839). **T038 (live screenshot loop, quickstart.md §2 steps 1–7 + §3) has not been run yet.** FR-004, FR-005, SC-001 and SC-005 depend on real layout, so they stay unverified until it runs. Spec status stays Draft until then (T039).
- **Live verification (2026-09-25)**: the user's live screenshots from the day's rounds covered quickstart §2 steps 1–2. The row sat on one line (Home → title → weather → site card), and the site card appeared beside the weather. Those rounds also drove three amendments: the card now appears when Lucy confirms the place (FR-003); the card is now the shield and the bold site name, with the level and its reason in the shield's tooltip (FR-006, FR-010, FR-013); and a 503 is now retried long enough to outlast a deploy restart, so the weather card no longer stays "Weather unavailable". Steps 3–7 were verified in later rounds (see the entries below). Only the §3 screen-reader check was not run; the user accepted the feature without it. Spec marked Implemented at the user's sign-off.
- **Follow-up screenshot (2026-09-25, production, dark theme, ~2560 px wide)**: this one confirms step 2 and step 6.
  - Step 2: the card shows a green check-shield and **Al Safa Park 2** in bold, with no other text. Hovering the shield shows "High confidence" and the outline's source. The weather card reads 36°C, so it recovered.
  - Step 6: with Solar Analysis on, the camera-attitude widget sits directly under the row.
  - Step 3 (a second screenshot, light theme): the Home button and all three cards switched to the same light surface together and stayed on one row. The green shield is still clearly visible.
  - Step 4 (the user's resize screenshots, production): as the window narrows, the row wraps onto two and then three lines. Nothing overlaps the top-right buttons. The weather card's place name is cut short with an ellipsis. One thing outside this spec: at narrow widths the compass widget overlaps the header of the Building Corrections panel.
- **Steps 5 and 7 (2026-09-25, signed in to /studio on the local stack, real Edge, both themes)**:
  - Step 5: a Medium site shows an amber shield and a Low site a red one, in both themes. Hovering the shield shows the level, e.g. "Low confidence — approximate", then the reason. The tooltip sits under the shield, over the map, and covers none of the row.
  - Step 7: stopping `viewer.boundary-confidence` removes the card. Home (x=24) and the weather card (x=212, "35°C, Clear") keep exactly the same boxes. The row's height stays 40 px at y=24.
  - An earlier run of the same checks, before sign-in worked, mounted the row on a bare page and gave the same colours and boxes.
  - Only §3 (the screen-reader check) remains unrun.
