# Contract: Capabilities and Offers

The full rationale is in [research.md](../research.md) D6–D8.

## `edit_site_boundary` (NEW)

| Property | Value |
|----------|-------|
| `Name` | `edit_site_boundary` |
| `Label` | "Edit the outline" |
| `Description` | Opens the map's outline editor so the user can move, add or delete the outline's corners. |
| `WhenToUse` | The user wants to adjust, fix, correct or redraw the outlined site's shape by hand. |
| `ArgumentHint` | none |
| `InputSchemaJson` | `{"type":"object","properties":{}}` |
| `ExpectedDuration` | Brief (no network) |
| `Area` | Location |
| `RiskLevel` | Low |
| `AcknowledgementTemplate` | "Opening the outline editor." |
| `IsAvailable` | `context.ActiveBoundary is not null` |
| `IsOfferable` | `false`. Offered only by `SiteBoundaryEditOffer`. |

`ExecuteAsync`:

- Loads the chat. If it is not the caller's, it fails with "Only the chat's owner can edit its
  outline." If there is no outline, it fails with "There's no outlined site to edit."
- Otherwise it returns:

```json
{ "siteName": "Muscat Grand Mall", "areaSquareMeters": 48860, "isHandEdited": false, "revision": "…", "openEditor": true,
  "howToEdit": "Drag a corner to move it, drag an edge's middle handle to add one, right-click or long-press a corner to delete it. Press Done when finished." }
```

`UsageGuidance`: "Say in one or two sentences that the editor is open and how to use it, from
howToEdit. Don't give the area." The narrator sees only this JSON.

`StructuredPayloadExtractor`: when `openEditor` is true, it emits
`ChatStreamChunk.SiteBoundaryEdit = new SiteBoundaryEditCommand(chatId, revision)`. See
[site-boundary-edit-sse-event.md](site-boundary-edit-sse-event.md).

## `reset_site_boundary` (NEW)

| Property | Value |
|----------|-------|
| `Name` | `reset_site_boundary` |
| `Label` | "Reset to Lucy's outline" |
| `WhenToUse` | The user wants their hand-edited outline undone, reset or replaced with the one Lucy found. |
| `ExpectedDuration` | Brief |
| `AcknowledgementTemplate` | "Putting back the outline I found." |
| `IsAvailable` | `context.ActiveBoundary is { Source: UserCorrected }` |
| `IsOfferable` | `false`. Offered only after a reused correction (below). |

`ExecuteAsync`:

- Sends `ResetSiteBoundaryCommand(chatId, expectedRevision: effective revision)`, the same
  handler as the REST reset.
- Returns the found outline as `SiteBoundaryPayload.Write(...)`, plus `"resetFromHandEdit":
  true`.
- The existing extractor path emits the `siteBoundary` event, which redraws with the animated
  border.
- The handler has already persisted the chat line.

## `set_site_boundary_members` (existing): changes

- It accepts `"keep": true`. When the given `memberIds` equal the currently included set, it
  returns `{ "kept": true, "outlineCovers": [...], "areaSquareMeters": … }` and no geometry, so
  no redraw event.
  - `UsageGuidance` adds: "When kept is true, say the outline stays as it is, in one sentence."
- `AcknowledgementTemplate` becomes "Now updating the site outline."
- On a hand-edited outline, composition goes through the join and cut path (research D9). The
  result carries `"handEdited": true`, and `UsageGuidance` adds: "When handEdited is true, say
  the user's hand edits were kept."

## `resolve_site_boundary` (existing): correction reuse

Before resolving, it calls `SiteBoundaryCorrectionMatcher` for this user, this name, and the
active location point. On a match:

- it links the correction and copies its found snapshot into the chat's `ActiveBoundary`;
- no Overpass or vision call is made;
- it returns the effective outline payload plus `"userCorrected": true`;
- `UsageGuidance` adds: "When userCorrected is true, say you're showing the user's own corrected
  outline and that it can be reset to the one you found."

## Offers (`ConversationTurnOrchestrator.EmitOfferIfDueAsync`)

The order of precedence is first match wins:

| # | Condition this turn | Offer |
|---|---------------------|-------|
| 1 | `resolve_site_boundary` ran, returned `userCorrected`, and the outline is hand-edited | **Reset offer**: question "This is your corrected outline." Rows: `reset_site_boundary`, then the generic analysis rows, then `Decline("Keep my outline")` |
| 2 | `resolve_site_boundary` ran and `SiteBoundaryMembershipOffer.Build` returns an offer | **Membership offer** (specs/077). Its last row is now `set_site_boundary_members {memberIds: <included>, keep: true}` labelled "Keep the outline as it is" (previously a `Decline`) |
| 3 | The outline became final and isn't hand-edited: `resolve_site_boundary` ran with no membership offer, **or** `set_site_boundary_members` ran (keep included) | **Edit offer** (`SiteBoundaryEditOffer.Build(boundary, analysisRows)`), described below |
| 4 | Otherwise | The existing generic offer, unchanged |

The edit offer's question depends on the confidence level:

- High: "I'm confident about this outline — want to adjust its corners?"
- Medium: "I'm fairly sure about this outline — want to adjust its corners?"
- Low: "I'm not sure about this outline — want to adjust its corners?"

Its rows are `edit_site_boundary` ("Edit the outline"), then the analysis rows the generic
generator returns for this turn (possibly none), then `Decline("It looks right")`.

In rows 1 and 3, "analysis rows" are `offerGenerator.GenerateAsync(...)`'s actions, after the
existing suppression rules. The generator's question is discarded.

**Tests**:

- `SiteBoundaryEditOfferTests`: question per confidence level, row order, analysis rows
  appended.
- `SiteBoundaryMembershipOfferTests`: the keep row is real, with the included ids.
- `ConversationTurnOrchestrator` offer-precedence tests: rows 1–4.
- `EditSiteBoundaryCapabilityTests` and `ResetSiteBoundaryCapabilityTests`: owner, missing
  outline, result JSON.
- `SetSiteBoundaryMembersCapabilityTests`: the keep and hand-edited cases.
- `ResolveSiteBoundaryCapabilityTests`: reuse, no reuse for the same name in a different place,
  and never another user's correction.
