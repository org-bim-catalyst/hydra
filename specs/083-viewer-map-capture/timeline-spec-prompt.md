# Timeline / Historical Imagery — `/speckit-specify` prompt

Paste the block below into a fresh chat with `/speckit-specify`. It is written to be
self-contained: the new chat will not know the 083 conversation, so everything it needs is in
the text.

---

## The prompt to paste

```
Historical imagery timeline: show how a place changed over time.

Extend the existing "map snapshot" capability (shipped in specs/083-viewer-map-capture) so
Lucy can return the aerial imagery of a place for a chosen time, not only the most recent
imagery. The platform already has: a geocoding provider (IGeocodingProvider), a map capture
pipeline (MapCaptures: CaptureMapQuery -> IMapImageRenderer -> Google Static Maps, with
ESRI World Imagery as the satellite-only fallback), image cropping/composition with
Magick.NET, a service API-key auth scheme, and a "map_snapshot" conversation capability
(src/AskLucy.Application/Conversations/Capabilities/MapSnapshotCapability.cs) that returns
a stored Document id, streamed to the client as a "snapshot" SSE event and rendered as its
own image bubble in the chat. This feature extends that pipeline; it does not replace it.

User-visible behaviour:

1. No date given -> today's behaviour exactly: the current snapshot, one image, no
   timeline. The wording of the assistant's reply must not promise a date it does not have.
   Important constraint: the live imagery providers (Google Static Maps, ESRI World
   Imagery) do NOT report a capture date. "The current date of the site" is therefore not
   readable from them. The reply must say the imagery is the provider's most recent
   available capture, and must not invent an acquisition date.

2. A specific date given ("a snapshot of Al Safa Park 2 from June 2023") -> the image for
   that date, or the closest available vintage on the correct side of the date, with the
   actual date of the image shown to the user. If nothing is near enough, say so plainly
   and offer the nearest available rather than silently substituting a different date.

3. A start and end date given ("every snapshot of X between 2020 and 2024") -> every
   available vintage in that range, in chronological order, each labelled with its real
   date, including the gaps (a period with no coverage must be visible as a gap, not
   silently skipped).

4. A timeline / carousel UI for one site over a time range: the user can step through the
   vintages (previous/next, a date scrubber, or a strip of thumbnails) and see the capture
   date of the image currently shown. It must work on phone width, be keyboard
   navigable, and have accessible labels (not colour alone, not a bare drag-only
   interaction). Arabic and English (the platform ships both; the i18n catalogs live under
   src/AskLucy.Web/ClientApp/src/i18n/messages/{en,ar} and every new string needs both).

Data / architecture constraints the spec must respect:

- The time dimension is a property of the IMAGERY SOURCE, not of the platform. The current
  providers have no history. Historical imagery needs a time-enabled source. The leading
  candidate is ESRI World Imagery Wayback (a WMTS tile service with one release number per
  vintage, plus a paired metadata feature layer that reports the real ACQUISITION date,
  provider, resolution and accuracy at a point). The spec must decide explicitly which
  source(s) are supported, must treat the acquisition-date metadata layer as the only
  authoritative source of a capture date, and must state plainly that a Wayback RELEASE
  date is not the ACQUISITION date (only part of the world is updated in any one release).
  The current ESRI fallback must keep working unchanged for date-less snapshots, so the
  time-enabled path is additive.

- The abstraction must stay vendor-agnostic, the same way the capture pipeline already is:
  Google primary, ESRI fallback, one router behind IMapImageRenderer. A second, parallel
  "historical renderer" that bypasses that router is a second drifting implementation of
  something that already exists. The time dimension is an input to the capture request, not
  a new pipeline.

- No new NuGet package unless the platform genuinely cannot do it. The platform already
  references Magick.NET for cropping/composition, and already has MediatR, FluentValidation,
  and the existing ProblemDetails / rate-limiting / options conventions.

- Tile stitching: a Wayback vintage is served as tiles, not as a single "export" image like
  the current ESRI export. The spec must decide how the tiles for the requested extent are
  fetched and composed into one image, and how it fails when a tile is missing (no silent
  partial image — constitution section 8, no silent failures).

- Attribution: every historical image must carry the imagery source and its licence
  requirements (Esri/Maxar/Nearmap attribution) on the image or in the surrounding UI. The
  spec must state where the attribution is rendered.

- Persistence and ownership: a timeline of N images is N stored documents. The spec must
  decide whether the images are persisted as platform Documents (like the current snapshot)
  or streamed, what the retention is, and how a user re-opens a timeline later. Persistence
  as Documents is the natural default, because a chat message must survive a reload.

- Cost and rate limits: historical capture is many more provider requests than a single
  snapshot. The spec must set a bound on the number of vintages one request may return and
  on the number of tiles one image may stitch, and must respect the existing per-caller
  rate-limit policy.

- Scale: a range covering a decade of imagery could be dozens of vintages. The spec must
  set a maximum and say what happens when a request exceeds it (e.g. return the most recent
  N in the range plus a clear statement that the range was truncated).

Out of scope for this feature: video/animation export of the timeline, ground-level or
street-level historical imagery, historical imagery for locations other than the requested
point, and any write-back of the historical images into knowledge bases (the current
snapshot already covers the single-image case).

Explicitly testable acceptance examples the spec should include:
 - "give me a snapshot of Al Safa Park 2" -> exactly one image, current imagery, and the
   reply does NOT state a capture date.
 - "show me Al Safa Park 2 in June 2023" -> one image, labelled with the real acquisition
   date of the vintage used, and a statement when the nearest vintage is not in June 2023.
 - "what did Al Safa Park 2 look like between 2020 and 2024" -> an ordered set of images,
   each labelled with its real date, with the uncovered periods visible as gaps.
 - a range that would return more than the maximum -> a truncated, clearly-labelled result.
 - a location with no historical coverage at all -> a plain "no imagery for that date
   range" message, not an empty timeline and not a current image presented as historical.
```

---

## Why the prompt says what it says

Two facts drove the wording, and both are easy to get wrong:

**1. The live providers have no acquisition date.** Google Static Maps and ESRI World
Imagery return pixels; neither reports when the imagery was flown. So the "no date given"
case cannot say "here is the site as of today" — the honest wording is "the provider's most
recent available capture". The prompt makes this an explicit constraint so the spec cannot
quietly promise a date the platform cannot read.

**2. A Wayback release date is not an acquisition date.** ESRI's own documentation says the
archive is keyed on *publication* in the World Imagery basemap, and only part of the world
is actually updated in any one release. The real acquisition date lives in the paired
metadata feature layer. The prompt requires the spec to treat that layer as the only
authority, so a timeline cannot be built on a date that is systematically wrong.

## The Google Custom Search question (answered)

The file's suggestion — `site:` plus `after:`/`before:` against the Custom Search JSON API
— **cannot** retrieve imagery by capture date. Those operators filter on Google's estimated
*page publication* date. They answer "which web pages about this site were published between
these dates", not "what did this site look like between these dates". Two extra limits: the
CSE date restriction is documented as "not very sensitive" (day-level filtering is
unreliable), and CSE image search filters on size/type/colour only — it exposes no capture
date at all. It is a reasonable tool for finding *articles about* a site's development, which
is a different feature; it is not a substitute for a time-enabled imagery source.

## Sources

- [Explore imagery archives using World Imagery Wayback (Esri)](https://www.esri.com/arcgis-blog/products/arcgis-living-atlas/mapping/use-world-imagery-wayback)
- [Esri/wayback-core](https://github.com/Esri/wayback-core) — `getWaybackItems`, `getMetadata`, `getWaybackItemsWithLocalChanges`
- [World Imagery Wayback (ArcGIS Online item)](https://www.arcgis.com/home/item.html?id=1dcae789f9c946368a6731ecf8ba5a75)
- [Custom Search JSON API reference](https://developers.google.com/custom-search/docs/json_api_reference) — `after:`/`before:`/`dateRestrict` are page-date filters
- [Filtering and sorting search results (structured search)](https://developers.google.com/custom-search/docs/structured_search) — `more:pagemap:` restricts, and why capture date is not among the exposed image attributes
