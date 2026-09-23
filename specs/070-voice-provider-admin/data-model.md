# Data Model: Voice Provider Administration

## VoiceProvider (table `VoiceProviders`)

| Column | Type | Rules |
|---|---|---|
| Id | uniqueidentifier | `Guid.CreateVersion7()` |
| ProviderKey | nvarchar | Required, trimmed, **unique**. Matches an `ITextToSpeechEngine.ProviderKey`. |
| DisplayName | nvarchar | Required, trimmed. |
| Priority | int | ≥ 0; 0 is Lucy's voice. Non-unique index. `SetPrimaryVoiceProvider` keeps the values dense (0..n-1). |
| DefaultVoiceId | nvarchar(100) | Nullable. Null means the engine's own configured default. |
| CredentialCiphertext | nvarchar | Nullable. Data Protection ciphertext; **never serialized**. |
| CredentialHint | nvarchar | Nullable. Vendor-style fingerprint from `CredentialHintFormatter` (specs/066). |
| CredentialLastRotatedAtUtc | datetime2 | Nullable. |
| Audit columns and RowVersion | — | Inherited from `BaseEntity`. |

### Invariants (`VoiceProvider`)

- `Create` rejects a blank key, a blank name or a negative priority with `DomainRuleViolationException`.
- `SetCredential` rejects blank ciphertext. The plaintext key never reaches Domain.
- `SetDefaultVoice` rejects a blank voice ID or one longer than 100 characters, and trims the value.
- `SetPriority` rejects a negative value. Setting the same value is a no-op and does not update the audit fields.

### Seed (migration `20260923063919_AddVoiceProviders`)

The migration inserts one row: key `ElevenLabs`, display name `ElevenLabs`, priority 0, no
credential, and `CreatedBy = system:seed`. With no stored credential, the configured
`ElevenLabs:ApiKey` is still used.

## Configuration (`Supertonic` section, all optional)

| Key | Default |
|---|---|
| ModelDirectory | `App_Data/Models/supertonic-3` |
| DefaultVoice | `F1` |
| TotalSteps | 8 |
| Speed | 1.05 |
| IntraOpThreads | 2 |
| MaxConcurrentSyntheses | 1 |
| Mp3BitRate | 96 |
