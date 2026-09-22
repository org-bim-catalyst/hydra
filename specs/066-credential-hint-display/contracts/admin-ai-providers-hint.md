# Contract Delta: Admin AI Providers — Credential Hint

Extends the existing contract documented in `specs/005-multi-provider-ai-engine/contracts/admin.md`. No routes, verbs, or request shapes change — only the response payload of the already-existing provider list read.

## `GET /api/v1/admin/ai/providers` (unchanged route)

Response array items gain one field:

```jsonc
{
  "id": "...",
  "providerKey": "openai",
  "displayName": "OpenAI",
  "isEnabled": true,
  "hasCredential": true,
  "credentialHint": "sk-p...33IA",   // NEW — null when hasCredential is false
  "credentialLastRotatedAtUtc": "...",
  "defaultModelId": "...",
  "healthStatus": "Healthy",
  "healthStatusCheckedAtUtc": "...",
  "healthFailureKind": null,
  "healthFailureReason": null,
  "healthStaleAfterUtc": "..."
}
```

**Invariant**: `credentialHint` is `null` if and only if `hasCredential` is `false`. The full API key value is never present in this or any response.

## `PUT /api/v1/admin/ai/providers/{id}/credential` (unchanged request/response shape)

No visible contract change (still `{ apiKey: string }` in, `204 No Content` out) — internally, the handler now also computes and persists `CredentialHint` alongside the ciphertext, so the *next* `GET providers` call reflects the new hint.

## `DELETE /api/v1/admin/ai/providers/{id}/credential` (unchanged)

No visible contract change — internally also clears `CredentialHint`.
