# Research: Reply Action Bar

## Decision 1: Layout mechanism for the relocated action row

**Decision**: Render the action row as a normal-flow sibling `Box`/`Stack` immediately after
the message `Paper`, inside the same outer flex container that already aligns the bubble
(`justifyContent: isUser ? 'flex-end' : 'flex-start'`), rather than keeping `position: absolute`
inside the `Paper`.

**Rationale**: The current control uses `position: absolute; right: 8; bottom: 8` *inside* the
`Paper`, which is why it floats over/inside the bubble and requires the bubble to reserve
`pb: 4` padding for it (spec FR-004's problem statement). Moving it to a sibling element below
the bubble, inside the same outer flex column, reproduces the ChatGPT/Claude pattern exactly:
the action row is document flow, left-aligned under the bubble regardless of `isUser` alignment
of the bubble itself (spec FR-005), and the `pb: 4` bubble padding hack is removed entirely.

**Alternatives considered**:
- Keep `position: absolute` but move it below the `Paper`'s bottom edge (negative offset) —
  rejected: still fragile against content reflow/resize and doesn't participate in the message
  list's own vertical spacing, so it can visually collide with the next message.
- CSS Grid two-row layout for bubble+actions — rejected as unnecessary complexity; a `Stack`
  (already used elsewhere in this file for attachments/citations) is sufficient and consistent
  with the file's existing patterns.

## Decision 2: Clipboard write + visible confirmation, no silent failure

**Decision**: Use `navigator.clipboard.writeText(message.content)`, `await`ed inside the
button's `onClick`, with local component state (`'idle' | 'success' | 'error'`) driving a
transient visual change (icon swap + `Tooltip`/`Snackbar`-style confirmation) that auto-resets
after a short delay. The `try/catch` around the `await` is mandatory per CLAUDE.md's Error
Handling section — a caught-and-discarded rejection is explicitly forbidden project-wide.

**Rationale**: `navigator.clipboard.writeText` returns a Promise that rejects on permission
denial or an insecure context; this project's constitution and CLAUDE.md both forbid an
unhandled rejection or a catch-and-discard. Driving success/error off the promise's own
resolution/rejection (rather than assuming success) satisfies FR-002/FR-003 without inventing
a new global toast subsystem — a local per-button state is sufficient since the confirmation is
scoped to "did *this* copy action just work."

**Alternatives considered**:
- Fire-and-forget `navigator.clipboard.writeText(...)` with no `await`/`catch` — rejected
  outright: this is precisely the unhandled-rejection pattern CLAUDE.md's Error Handling
  section forbids.
- `document.execCommand('copy')` fallback for older browsers — rejected: the project's
  supported-browser baseline (per Technical Context) already covers the async Clipboard API;
  adding a legacy fallback is scope creep not requested by the spec.
- App-wide toast/snackbar service for the confirmation — rejected as over-engineering for a
  single-button, per-message confirmation; no such shared service currently exists in this
  codebase for this class of transient feedback, and introducing one is out of scope.

## Decision 3: Icon choice from the existing library

**Decision**: `RiFileCopyLine` (outline style, consistent with the "Line" family already
implied by the file's other icons) for the idle Copy state, swapping briefly to `RiCheckLine`
on success, from the already-installed `@remixicon/react` package — no new dependency.

**Rationale**: Spec Assumptions and FR-006 require the same icon library already in use
(`@remixicon/react`, confirmed installed and exposing `RiFileCopyLine`/`RiCheckLine`). Swapping
to a checkmark on success is the standard, unambiguous "it worked" affordance in this icon
family and mirrors `RiPlayFill`/`RiStopFill`'s existing swap-on-state pattern already used for
Replay/Stop in this same file.

**Alternatives considered**: A separate MUI `Snackbar` for the confirmation instead of an
icon swap — not excluded, but the icon-swap-plus-`Tooltip` approach is simpler, matches the
"brief, visible confirmation" language of FR-003 without adding another moving part, and keeps
the confirmation co-located with the action that produced it.

## Decision 4: Icon sizing

**Decision**: Reduce from the current `fontSize="small"` (MUI's `small` maps to 20px for Remix
icons rendered as SVG components at their default size) to an explicit smaller pixel size (16px)
on both the relocated Replay/Stop icons and the new Copy icon, via the `size` prop already
supported by `@remixicon/react` components (as used elsewhere in this file, e.g.
`RiAttachment2 size={18}`).

**Rationale**: FR-006 requires the action-row icons to be "visibly smaller" than the prior
in-bubble control. The file already has a working precedent (`RiAttachment2 size={18}` on the
attachment chip) for explicit pixel sizing via the same library's `size` prop, so no new sizing
mechanism is introduced — just a smaller value than the current control's implicit ~20px.

**Alternatives considered**: Shrinking via the MUI `IconButton`'s own `size="small"` alone
(already applied) — rejected as insufficient on its own since the icon glyph itself, not just
its clickable padding, must read as smaller per FR-006; the explicit `size` prop on the Remix
icon component is what actually shrinks the glyph.
