# Phase 0 Research: Replace Credential Modal Polish

No `[NEEDS CLARIFICATION]` markers remain in the spec (see `/speckit-clarify` — 0 questions asked, spec judged fully specified). The items below record the concrete implementation decisions the plan depends on.

## Decision: Dialog width

**Decision**: Set the credential `Dialog`'s `maxWidth` prop to `sm` (from MUI's implicit default of `xs` for an unset `maxWidth`) combined with `fullWidth`.

**Rationale**: MUI's `Dialog` without an explicit `maxWidth` renders at `xs` (444px). `sm` is 600px — roughly 1.35x, but combined with `fullWidth` (which makes the dialog fill that breakpoint's width rather than shrink-to-content) the *visible* rendered width roughly doubles from what a short "API key" `TextField` currently occupies unconstrained. `md` (900px) was rejected as disproportionate for a two-field form. On narrow viewports MUI's responsive dialog behavior (full-width minus margin) already satisfies FR-001's no-overflow requirement without extra work.

**Alternatives considered**:
- Hardcoded pixel width via `sx={{ '& .MuiDialog-paper': { width: Npx } }}` — rejected, fights MUI's responsive breakpoint system and risks overflow on narrow viewports (violates FR-001's second clause).
- `maxWidth="md"` — rejected as wider than needed for a title + one text field + placeholder.

## Decision: Show/hide toggle implementation

**Decision**: `TextField`'s `type` prop switches between `'password'` and `'text'` based on a local `showApiKey` boolean; a trailing `InputAdornment` containing an `IconButton` (`Visibility` / `VisibilityOff` from `@mui/icons-material`) is rendered only when `apiKeyInput.length > 0`.

**Rationale**: This is MUI's own documented pattern for a password-visibility toggle (`slotProps.input.endAdornment`), so it matches "standard email/password field" conventions the request asked for, with no new component or dependency. Conditioning the adornment's presence on `apiKeyInput.length > 0` directly satisfies FR-005/FR-006 (toggle appears only once something is typed, disappears if cleared).

**Alternatives considered**:
- Always rendering the toggle (disabled when empty) — rejected; the spec explicitly requires the icon to be *absent*, not merely disabled, when the field is empty (FR-006, Edge Cases).
- A separate reusable `PasswordField` component — rejected as premature; no second call site exists today (`grep` found no other password-visibility toggle anywhere in `ClientApp`), and this dialog is the only place the pattern is needed. Constitution §III (YAGNI) — do not build the abstraction until a second concrete caller exists.

## Decision: Placeholder text and empty-state

**Decision**: Add `placeholder="Please insert API key here"` to the `TextField`. No other change needed for the empty-state guarantee — `apiKeyInput` is already reset to `''` both on dialog open (`openCredentialDialog`) and on close (`closeCredentialDialog`), so the field never carries a stale or pre-filled value today; the only gap was the absence of placeholder text making an empty masked field visually ambiguous.

**Rationale**: Confirms FR-002/FR-003 require no state-management change, only a presentational one — verified by reading `AiProviderActionsMenu.tsx`'s existing `apiKeyInput` state handling before planning.

**Alternatives considered**: None — this was a direct reading of existing code, not a design choice with real alternatives.

## Decision: Scope confirmation — one component serves all providers

**Decision**: No per-provider branching needed. `AiProviderActionsMenu` is rendered once per row with a `provider` prop (confirmed via `AiProviderActionsMenu.test.tsx`, which exercises the same component against `Anthropic`-, credentialed, and non-credentialed provider fixtures); editing the one dialog satisfies FR-009 for all four providers automatically.

**Rationale**: Verified directly against the current component and its test file rather than assumed.

**Alternatives considered**: None needed — confirmed by reading the code.

## Output

All unknowns resolved. Proceeding to Phase 1 design.
