# SPEC-077: Site Boundary Membership and Capability Settings

**Status:** Implemented 2026-09-26
**Depends on:** specs/042-site-boundary-resolution, specs/045-conversational-agent-runtime, specs/076-building-conflation-height-raster

## Problem

"Show me BurJuman" outlined whatever one OSM way the resolver picked. BurJuman is really a
development: the mall, the BurJuman Business Tower and the Arjaan hotel share its walls, a
second "Burjman Office Tower" stands across the street, and the metro station carries its name.
Users could not say "just the mall podium", and could not add the tower across the street.

## Decision

1. **Capability settings, behind a gear.** The admin AI Capabilities table has a Settings
   column. Each row's gear is enabled when its capability declares settings, and dimmed and
   disabled when it declares none. Pressing it opens a dialog holding those settings. Settings
   are declared in code (`CapabilitySettingCatalog`) and stored per capability in
   `AiCapabilitySettings`; a capability with no stored row uses the declared default.
   The first and only setting is **Boundary vision → Include connected buildings of the same
   development** (default on).
2. **Membership discovery.** After a boundary is resolved and the setting is on,
   `SiteBoundaryMembershipService` asks `IRelatedSiteBuildingProvider` (Overpass) for named
   buildings and stations within reach of the site (up to 800 m). A building joins the list
   only when its name shares the site's core name (`SiteNameMatcher`: "BurJuman" matches
   "Burjman", generic words such as "mall" and place names are ignored).
   - **Connected**: within 2 m of the site (shares a wall). Included by default.
   - **Nearby**: up to 150 m away. Listed, not included. A station is always Nearby, even when
     it touches the site.
   - At most 6 members; the site's own way and anything inside it are skipped.
3. **The outline is redrawn from the chosen buildings.** `ISiteFootprintUnion`
   (`RasterSiteFootprintUnion`) rasterises the site and included members at 0.5 m, bridges
   seams narrower than 2 m, and traces the result. The ring holding the site becomes the
   boundary polygon; separate buildings (across a street) become `additionalPolygons`. The
   original site ring is kept as `corePolygon` so a later choice can start over from it.
4. **Lucy asks which buildings the site includes.** When members were found, the turn ends with
   a lettered offer, Claude-style:
   - A. BurJuman Mall only
   - B. BurJuman Mall with its connected buildings (shown now)
   - C. Also the buildings across the street
   - D. Everything, including the station
   - E. Other — I'll name the buildings
   - Keep the outline as it is

   Letters follow the groups actually found. A–D are all the new `set_site_boundary_members`
   capability with different `memberIds`; "Other" is a follow-up the user answers in words, which
   the same capability resolves by name (`memberNames`).

   After the choice, Lucy says what the outline covers, its new area, and what the choice added
   or removed. `set_site_boundary_members` returns `outlineCovers` (the site first, then its
   included buildings), `addedBuildings` and `removedBuildings`, worked out against the outline
   that was on screen, ahead of the geometry. It does not return `excludedBuildings`. The model
   that writes the reply sees only this result, not the chat. When it was given the excluded
   list, it called a building it had only offered "previously included", even when told not to.
   When the list held only the site's buildings and not the site, it said "the outline includes
   no buildings" after a resolve and "only Phase 2" after adding one.
5. **Setting off means no question.** Discovery is skipped entirely: the site alone is
   outlined and nothing is asked.

## Behaviour changes

- **Rows can share a capability key.** `SelectedActionResolver` tells same-key rows apart by
  comparing the client's arguments with each row's (`JsonNode.DeepEquals`); what is dispatched
  is still the matched row's own arguments. On reload the chat names the picked row by its
  persisted label.
- **A site can be several rings.** The SSE `siteBoundary` event, `ChatDetailDto.activeBoundary`
  and the client store carry `additionalPolygons`. The map draws every ring with its own animated
  border (`SiteBoundaryRenderer.setRings`); the solar dome and building-fetch radius (specs/076)
  cover all of them.

## Found testing outside Dubai (Muscat, 2026-09-26)

Seven Muscat landmarks were tried to check that nothing here is Dubai-specific. Five bugs showed up
and were fixed:

- **Theatres and mosques were never candidates.** The Royal Opera House and the Grand Mosque are
  mapped as `amenity=theatre` and `building=mosque`. `OverpassBoundaryCandidateProvider` now also
  queries `amenity` `place_of_worship` and `theatre`, and `building` `mosque`, `cathedral`,
  `church` and `temple`. Only these named landmark `building` values are queried, never
  `building=*`.
- **Mutrah Souq picked the mosque beside it.** The souq's `name` is Arabic and its English name is
  in `int_name`, which nothing read. `BoundaryCandidateScorer` and `SiteBoundaryMembershipService`
  now read `name`, `name:en`, `int_name`, `official_name` and `alt_name`. The scorer also treats
  "souq" as a retail word, like "mall".
- **Al Alam Palace took a trace of the wrong block.** Gemini traced a block about 250 m east. It
  passed the area and distance checks, so it replaced the palace's mapped outline.
  `TryBuildVisionTracedGeometry` now also requires the trace and the mapped outline to overlap:
  at least 25% of the smaller one must lie inside the other (`GeometryMath.OverlapFraction`).
  The rendered-fill path for parks does not apply this check.
- **The second outline was hard to see.** Choosing "Muscat Grand Mall with its nearby buildings"
  added Phase 2 as a separate ring, but only the main ring got the animated border. Phase 2 had
  only the faint fallback fill. Every ring now gets its own border.
- **Typing a choice failed the turn.** "Show me the Mall only" ended in "Something went wrong
  and I couldn't finish", though picking a row worked. Typed requests run each step on its own
  task, alongside the controller saving the "Now redrawing" line. The step's narration used the
  request's AI provider, which reads its credential through the request's `DbContext`. This
  capability makes no network call, so its narration reached the database while that save was
  still running: "a second operation was started on this context instance". `SubAgentDelegator`
  now narrates with a provider resolved in the step's own scope
  (`ConversationTurnRequest.ProviderKey`). Rows run in order on the request itself, so they
  never raced.

Results after the fixes:

| Site | Area | Source |
|------|------|--------|
| Muscat Grand Mall | 34,065 m² (48,860 m² with Phase 2) | OSM, with Phase 2 offered as nearby |
| Oman Avenues Mall | 25,764 m² | OSM |
| Royal Opera House Muscat | 20,297 m² | OSM |
| Mall of Oman | 90,718 m² | OSM |
| Sultan Qaboos Grand Mosque | 8,608 m² (the building) | OSM |
| Mutrah Souq | 14,205 m² | OSM `landuse=retail`, with Muttrah Gold Market offered as nearby |
| Al Alam Palace | 20,673 m² | OSM |

## API

| Method | Route | Permission | Body / result |
|--------|-------|------------|---------------|
| GET | `/api/v1/admin/ai/capabilities/settings` | `admin.ai-capabilities.view` | Every capability with its declared settings (`key`, `valueType`, `label`, `description`, `value`, `defaultValue`); empty `settings` when it declares none |
| PUT | `/api/v1/admin/ai/capabilities/{capability}/settings` | `admin.ai-capabilities.manage` | `{ "values": { "includeConnectedBuildings": "false" } }` → 204. Unknown keys and mistyped values → 400 |

## Database

Migration `20260926125557_AddCapabilitySettingsAndSiteBoundaryMembers`:

- New table `AiCapabilitySettings` (`Capability` nvarchar(40), `Key` nvarchar(100), `Value`
  nvarchar(500), audit columns, `RowVersion`); unique filtered index on (`Capability`, `Key`)
  where `DeletedAtUtc IS NULL`.
- `UserChats` gains nullable `ActiveBoundaryCorePolygonJson`, `ActiveBoundaryAdditionalPolygonsJson`
  and `ActiveBoundaryMembersJson`. Existing chats read as a single-ring site with no members.

Applied automatically at startup. No backfill: with no stored row the setting reads its default (on).

## Verification

- Application: `SiteNameMatcherTests`, `GeometryMathGapTests` (including `OverlapFraction`),
  `SiteBoundaryMembershipServiceTests`, `SiteBoundaryPayloadTests`,
  `SiteBoundaryMembershipOfferTests`, `SetSiteBoundaryMembersCapabilityTests` (including
  added/removed reporting), `CapabilitySettingsTests`, same-key cases in
  `SelectedActionResolverTests`, the souq/`int_name` case in `BoundaryCandidateScorerTests`, and
  the no-overlap trace case in `BoundaryResolutionServiceTests`.
- Infrastructure: `RelatedSiteBuildingsTests` (Overpass interpretation, station entrances mapped
  onto their hall, seam bridging), and the theatre/place-of-worship query in
  `OverpassBoundaryCandidateProviderTests`.
- Frontend: gear and dialog in `CapabilityAssignmentsSection.test.tsx`, `siteRingsOf`, multi-ring
  dome reach, same-key label resolution in `ChatPage.test.tsx`, and one border per ring in
  `SiteBoundaryRenderer.test.ts`.
