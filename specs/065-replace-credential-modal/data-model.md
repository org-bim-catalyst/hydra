# Phase 1 Data Model: Replace Credential Modal Polish

This feature introduces no persisted entity, no new API payload, and no schema change. It is a presentational and local-component-state change only.

## Existing entity referenced (unchanged)

**AI Provider Credential** — already defined by the existing `setCredential(providerId, apiKey)` / `clearCredential(providerId)` calls in `adminAiProvidersApi`. This feature does not add, remove, or rename any field on this entity or its API contract; it only changes how the *not-yet-submitted* value is displayed while the administrator is typing it.

## New local UI state (component-scoped, not persisted)

`AiProviderActionsMenu.tsx` gains one new piece of component state alongside the existing `apiKeyInput`:

| State | Type | Initial value | Reset points |
|---|---|---|---|
| `showApiKey` | `boolean` | `false` | Reset to `false` whenever the dialog opens (`openCredentialDialog`) or closes (`closeCredentialDialog`) — mirrors how `apiKeyInput` already resets, so visibility never carries over between dialog sessions (FR-010). |

No other state changes. The toggle's visibility (rendered or not) is derived directly from `apiKeyInput.length > 0` — not itself stored state.

## Validation rules

Unchanged. The existing "an API key is required" check (`handleCredentialConfirm`) still fires on empty submit; this feature does not alter validation, only display.
