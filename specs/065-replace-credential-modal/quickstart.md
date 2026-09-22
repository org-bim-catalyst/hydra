# Quickstart: Replace Credential Modal Polish

Manual verification guide for `specs/065-replace-credential-modal`. No backend interaction is required — every scenario below is exercisable against the running frontend dev server with any provider row, since `AiProviderActionsMenu.tsx` is shared across all four providers.

## Prerequisites

- Frontend dev server running (`ClientApp`), signed in as an admin/built-in-admin user.
- Navigate to **Admin → AI providers**.

## US1 — Reveal/hide toggle appears only when typed (P1)

1. Click the ⋮ menu on any provider row → **Set credential** (or **Replace credential** if one is already configured).
2. Confirm: the API key field is empty, no eye icon is visible.
3. Type a character. **Expected**: an eye icon appears inside the field, trailing edge, matching a standard password field's toggle position.
4. Click the eye icon. **Expected**: the typed value becomes visible plain text; icon changes to indicate "hide."
5. Click it again. **Expected**: value is masked again.
6. Select all typed text and delete it. **Expected**: the eye icon disappears once the field is empty.

## US2 — Empty field never looks pre-filled (P1)

1. Find a provider row where **Credential** already reads "Configured" (e.g., Anthropic if seeded).
2. Click ⋮ → **Replace credential**. **Expected**: dialog title reads "Replace credential for {Provider}"; the field is empty and shows placeholder text "Please insert API key here" — not a masked dot/bullet value.
3. Close the dialog without typing anything (Cancel).
4. Reopen **Replace credential** for the same provider. **Expected**: field is empty again with the placeholder shown — the previous session's state (if any had been typed) does not persist.

## US3 — Double-width dialog (P2)

1. On a standard desktop browser window (≥1280px wide), open the credential dialog for any provider.
2. **Expected**: the dialog is visibly wider than the previous (~444px) width — roughly matching MUI's `sm` breakpoint (~600px) — while remaining centered and readable.
3. Resize the browser to a narrow/mobile width (e.g., 375px) and reopen the dialog.
4. **Expected**: no horizontal overflow or clipped content; the dialog fills the available width responsively.

## Regression checks

- Submitting a typed value still calls `setCredential` and closes the dialog (unchanged behavior).
- Submitting with an empty field still shows the "An API key is required" error and does not call the API.
- The submitted value is exactly what was typed, regardless of whether the toggle was used to reveal it beforehand.
- Behavior in all steps above is identical across Anthropic, Google Gemini, OpenAI, and OpenRouter rows (FR-009) — spot-check at least one provider other than the one used above.
