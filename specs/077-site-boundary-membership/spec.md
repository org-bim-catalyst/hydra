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
5. **Setting off means no question.** Discovery is skipped entirely: the site alone is
   outlined and nothing is asked.

## Behaviour changes

- **Rows can share a capability key.** `SelectedActionResolver` tells same-key rows apart by
  comparing the client's arguments with each row's (`JsonNode.DeepEquals`); what is dispatched
  is still the matched row's own arguments. On reload the chat names the picked row by its
  persisted label.
- **A site can be several rings.** The SSE `siteBoundary` event, `ChatDetailDto.activeBoundary`
  and the client store carry `additionalPolygons`. The map draws every ring; the solar dome and
  building-fetch radius (specs/076) cover all of them.

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

- Application: `SiteNameMatcherTests`, `GeometryMathGapTests`, `SiteBoundaryMembershipServiceTests`,
  `SiteBoundaryPayloadTests`, `SiteBoundaryMembershipOfferTests`,
  `SetSiteBoundaryMembersCapabilityTests`, `CapabilitySettingsTests`, and same-key cases in
  `SelectedActionResolverTests`.
- Infrastructure: `RelatedSiteBuildingsTests` (Overpass interpretation, station entrances mapped
  onto their hall, seam bridging).
- Frontend: gear and dialog in `CapabilityAssignmentsSection.test.tsx`, `siteRingsOf`, multi-ring
  dome reach, and same-key label resolution in `ChatPage.test.tsx`.
