# Contract: Schematic Site Map Prompt

**Spec**: SPEC-057 | **Requirements**: FR-028, FR-029, FR-030

A versioned prompt artifact. Per constitution §9, prompts are reviewed like code and MUST NOT live as inline
string literals in a handler. The specialist loads this template and substitutes its placeholders; changing the
wording is an edit to this file, reviewed in its own right.

## Version

| Version | Date | Change |
|---|---|---|
| 1 | 2026-09-17 | Initial. Authored from the project brief. |

## Placeholders

| Token | Source |
|---|---|
| `{siteName}` | `SiteAnalysis.SiteName` — the resolved site's display name |
| `{siteLocation}` | Human-readable location of the resolved site (locality/region), derived from the resolution result |

Both are **platform-resolved values, not raw user input** — the site has already passed through boundary
resolution before an analysis is dispatched. Substitution is plain replacement; the values are not treated as
instructions (constitution §8).

## Template (v1)

```text
Generate a top-down site analysis map of an urban area for {siteName}, located at {siteLocation}.
Show streets, major roads, rivers/canals, green spaces, and built-up areas. Use color coding:
light gray for buildings, blue for water bodies, green for parks, and yellow/orange for major
roads. Include contour or flow lines where appropriate to indicate water paths. Make the map
schematic, clear, and visually focused, with minimal text labels. Center it on the main
site/feature and use a clean, modern urban-planning style. Include a clear legend describing the
visual elements.
```

## Generation settings

| Setting | Value |
|---|---|
| Provider + model | The `ImageGeneration` AI capability assignment (Admin → AI Capabilities) — a provider **and** a pinned image-capable model (`AIModel.SupportsImageOutput`) |
| Fallback | None — unassigned or unusable reports `AiCapabilityNotConfiguredException`; a chat model is never used |
| Resolution | Provider default unless configured |

Resolved through `IImageGenerationService` (`AiCapabilityProviderResolver` → `IAIProviderResolver.Resolve(providerKey)`)
— never by referencing a concrete provider type (constitution §9). OpenAI and Google Gemini implement image
generation; Anthropic and OpenRouter throw `NotSupportedException`. The provider's response — hosted URL,
base64, data URL or binary (`GeneratedImagePayload`) — is normalised by `GeneratedImageMaterializer`, which
verifies the format from the bytes (PNG/JPEG/WebP) before it is stored.

> **Superseded (2026-09-18):** this contract originally specified a `SiteAnalysisOptions` config section. It
> bypassed the capability assignments every other AI task uses and hard-coded the model in a config file; it
> was removed in favour of the `ImageGeneration` capability.

## Output handling (FR-029)

`IAIProvider.GenerateImageAsync` returns a **transient, provider-hosted URI**. It MUST NOT be given to the
client — `imageBlockSchema` rejects external addresses outright, and doing so would leak a third-party URL into
user-facing content.

Required sequence:

1. Download the bytes from the returned URI.
2. Persist through `DocumentUploadFinalizer.FinalizeAsync(ownerId: analysis.UserId, fileName, content,
   sizeBytes, actor, ct)` — this performs magic-byte validation, storage-quota enforcement, checksum
   de-duplication, and `Document`/`DocumentVersion`/`DocumentChecksum` creation together (research D8).
3. Report the resulting `DocumentId` to the relay, which composes an `ImageBlock { fileId, alt }`.

`alt` text is required by the block schema (1–300 chars) and MUST describe the map meaningfully for screen
readers (constitution §7 accessibility) — e.g. *"Schematic top-down site analysis map of {siteName}"*.

## Provenance reported

| Field | Value |
|---|---|
| `DataSource` | `"{providerKey}:{model}"` |
| `ConfidenceLevel` | `Medium` |

`Medium` because the site and its coordinates are exact, but the rendering is a generative interpretation rather
than surveyed data. This is a labelling honesty requirement (FR-011), not a quality judgement about the image.

## Failure handling (FR-030)

Catch `NotSupportedException` (provider cannot generate images) and the existing `AiProviderException` hierarchy
(`Unavailable`, `CredentialRejected`, `RateLimited`, `QuotaExhausted`, `UsageRestricted`, `RequestInvalid`,
`ResponseNotUnderstood`). Do **not** introduce new exception types.

On any of these: report failure to the relay with the classified reason, and return `AgentToolResult.Failure`.
Never deliver an empty or broken image finding. The tolerant `AnyCompleted` merge strategy keeps this branch's
failure from failing the analysis (research D13).

## Verification item

Confirm `IDocumentFileValidator` accepts PNG before relying on step 2. `DocumentFileType` declares `Png`/`Jpeg`,
but the validator's accepted-type set must be checked and extended if image uploads were previously restricted
to document formats.
