# ADR 0015: The Viewer Camera Belongs to the User

**Status:** Accepted

**Date:** 2026-09-22

**Feature:** [specs/038-viewer-poi-zoom](../../specs/038-viewer-poi-zoom/spec.md),
[specs/027-immersive-viewer-platform](../../specs/027-immersive-viewer-platform/spec.md)

## Context

Two long-standing complaints about the workspace map turned out to be one principle being violated
in two places: leaving `/studio` and returning changed the zoom, and the camera also drifted on its
own at unpredictable intervals while the page sat open.

Both were investigated to root cause from evidence — an exported console log carrying 987
instrumented camera writes, each with its stack — rather than from theory. Two earlier diagnoses
(a `setMapTypeId` call clobbering the camera; the map silently rendering as raster) were wrong and
are recorded as such so they are not re-proposed:

* The `setMapTypeId` fix was real and worth keeping — it removed a `styles property cannot be set`
  warning — but the camera was still destroyed identically afterwards.
* `map.getRenderingType()` returns `UNINITIALIZED` at construction and must be read later (on
  `idle`). Read correctly it reports `VECTOR`, and the camera was still destroyed identically. The
  console line *"As of Maps JavaScript API version 3.65, 45° imagery on raster maps…"* is an
  unconditional deprecation notice that appears on confirmed-vector maps too; it is not evidence
  about a map's rendering type.

The actual causes:

1. **Google Maps JS overwrites the camera it was constructed with.** `google.maps.Map` finishes
   wiring its camera *after* the constructor returns, and that wiring discards the camera passed as
   construction options. Verified from call stacks against Maps JS 3.66: heading is zeroed via
   `MVCObject.bindTo` → `heading_changed`; tilt via `bindTo` → `mapTypeId_changed` →
   `actualTilt_changed`; fractional zoom is snapped to a whole level (17.526 → 18) by the map's
   internal zoom handling. No application frame appears in any of those stacks, so there was no
   application behaviour to stop doing.

   Tilt already survived, because `MapRenderTarget` re-applies the view mode's tilt on every
   `tilt_changed`. Heading survived only by accident, when auto-rotation happened to be enabled.
   Zoom had no defence at all.

2. **Passive location tracking was treated as a new location.** `useGeolocation`'s `watchPosition`
   runs for the whole session so that permission revocation is detected. It therefore keeps
   emitting fixes that differ by a few metres of GPS drift. `ViewerSurface` depended on those
   coordinates in the effect that framed the camera, so every drifting fix re-ran
   `fitBounds`/`zoomToAltitude` and pulled the camera back off wherever the user had put it.

## Decision

**The camera belongs to the user.** Only two things may move it: an explicit user action, and a
location being *deliberately* established. Neither remounting the surface nor a fresh reading of
the place already on screen qualifies.

1. **`CameraRestoreGuard`** (`viewer/camera/cameraRestoreGuard.ts`) re-asserts a restored zoom and
   heading on each `idle`, giving them the durability tilt already had. It **releases the moment
   the map matches**, and is abandoned outright by the first drag or wheel. Both release paths are
   load-bearing: a guard that stayed armed would be indistinguishable from a broken map the next
   time the user moved. It defers heading to `rotationDriver` while that is running, and stops
   after 20 attempts rather than looping against a map that keeps overwriting the camera.
   `GoogleMapsGisLayerHandle.setCamera` was added so a correction is a single atomic write.

2. **Framing is a separate effect from content**, keyed on a *deliberately established* location
   rather than on coordinates. The device establishes a location once and then reports it
   indefinitely, so every geolocation fix shares one framing key; an agent naming a place is a
   deliberate act each time, so its coordinates and framing hints all form part of its key.
   Coordinates are read at call time rather than depended on, so drift cannot re-trigger framing,
   and the last framed key lives on `viewerSession` so a remount re-frames nothing. Recorded as
   [FR-001a](../../specs/038-viewer-poi-zoom/spec.md).

## Alternatives considered

**Pass the camera as construction options and trust it.** This is the documented Maps JS API and
what the code already did; the stacks above show it does not survive. Rejected on evidence.

**Re-apply the camera once, on a timer after construction.** Any fixed delay is a guess about when
Google's wiring finishes, and the browser gives no signal for it. The guard keys off `idle` — an
event the map actually emits — and re-checks rather than assuming a single write landed.

**Keep the guard armed for the lifetime of the map.** Rejected: it would fight every later
legitimate camera change, converting one bug into a worse one. Releasing on match and on first
gesture is what makes the guard safe to exist at all.

**Stop `watchPosition` once a location is known.** Rejected: it runs specifically so that
permission revocation is noticed mid-session (specs/027 FR-012). The fix belongs at the consumer
that wrongly treated its output as a new location, not at the source.

## Consequences

Returning to the workspace leaves the map pixel-identical, and the camera holds position
indefinitely while the page is open — both verified by screenshot comparison across a navigation
round trip and over an hour of idle time.

`CameraRestoreGuard` is deliberately generic over a `RestorableCameraTarget`, so a future non-Maps
render target with the same post-construction behaviour can reuse it without depending on Google's
handle type.

This ADR does not make the viewer immune to the underlying hazard: any new code that moves the
camera in response to *data arriving* rather than to the user will reintroduce the same class of
bug. The rule to apply in review is in the *Camera ownership* section of
`src/AskLucy.Web/ClientApp/src/viewer/README.md`.
