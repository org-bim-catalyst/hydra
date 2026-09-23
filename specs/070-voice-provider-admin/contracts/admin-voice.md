# Contract: Admin Voice API

The base route is `api/v1/admin/voice`, and every endpoint uses the `admin-endpoints` rate limit.
In the table, **view** means the `admin.ai-providers.view` permission and **manage** means
`admin.ai-providers.manage`.

Errors are returned as Problem Details:

| Condition | Status |
|---|---|
| Unknown provider or engine | 404 |
| Engine already added | 409 |
| Rule violation or failed validation | 400 |
| Provider failure | The status for the `AiProviderException` kind |

| Method | Route | Permission | Body | Returns |
|---|---|---|---|---|
| GET | `engines` | view | — | `VoiceEngine[]` |
| GET | `providers` | view | — | `AdminVoiceProvider[]`, in priority order |
| POST | `providers` | manage | `{ providerKey, apiKey? }` | 201 with `AdminVoiceProvider` |
| PUT | `providers/{id}/credential` | manage | `{ apiKey }` | `AdminVoiceProvider`; 400 for an on-server engine |
| GET | `providers/{id}/voices` | view | — | `VoiceOption[]` |
| PUT | `primary` | manage | `{ providerId, voiceId }` | `AdminVoiceProvider[]`, renumbered with the chosen provider first |
| POST | `providers/{id}/preview` | manage | `{ voiceId, text (≤ 500), language }` | `VoicePreview` |

## Shapes

```ts
interface VoiceEngine { providerKey: string; displayName: string; requiresCredential: boolean; isAdded: boolean }

interface AdminVoiceProvider {
  id: string; providerKey: string; displayName: string; priority: number; isPrimary: boolean
  defaultVoiceId: string | null; requiresCredential: boolean; hasCredential: boolean; credentialHint: string | null
}

interface VoiceOption { id: string; name: string; gender: string | null; description: string | null }

interface VoicePreview { audioBase64: string; contentType: 'audio/mpeg' }
```

The API never returns the credential value. A preview uses only the named provider, with no
failover. The text is limited to 500 characters, and the whole clip comes back base64-encoded.
