import * as THREE from 'three'
import type { DrawingSpaceHandle } from '../../../viewer/scene/DrawingSpaceRegistry'
import { buildFootprintMeshes, setShowMass, type FootprintBuildResult } from '../buildings/footprintGeometry'
import type { SiteBuildingDto } from '../api/siteBuildingsApi'
import { ShadowGround } from './shadowGround'
import {
  MIN_SHADOW_ELEVATION_DEGREES,
  NO_BUILDINGS_FALLBACK_EXTENT_METRES,
  SunLight,
  shadowRadiusMetres,
} from './sunLight'
import { solarPositionToEnuUnitVector } from '../solar/solarPosition'
import {
  SUN_PATH_DOME_RADIUS_METRES,
  buildDatedPath,
  buildFixedFurniture,
  sunPathDomeRadiusFor,
  updateCurrentPositionMarker,
} from './sunPathCurve'

/**
 * T035, research D8 — during continuous playback, a sun movement smaller than this is not drawn.
 * 0.25° is below the angular resolution of a shadow edge at any plausible viewing distance, and
 * the sun covers it in about one minute of real solar time, so a useful fraction of playback
 * frames is skipped without the shadows ever visibly lagging the sun-path marker (FR-018, FR-020).
 */
export const SHADOW_GATE_DEGREES = 0.25

/**
 * Owns the extension's own `DrawingSpaceHandle` and everything drawn inside it — the sun-path
 * dome, the buildings group, the shadow ground plane and the sun light. Nothing in this feature
 * ever holds a reference to the shared `THREE.Scene`, `Camera` or `WebGLRenderer` (FR-037); this
 * class is the single place that touches the drawing space's `group`.
 *
 * research D15 — the ground offset (FR-026) is applied by moving THIS group in Z
 * (`group.position.z`), never by re-anchoring the scene. Everything added as a child of this
 * group — dome, buildings, ground plane, light target — therefore moves together with one
 * assignment, and nothing outside the extension is affected.
 */
export class SolarScene {
  readonly drawingSpace: DrawingSpaceHandle

  /**
   * Sub-groups children are organized under, so each concern (T017 sun path, T037 light, T038
   * ground, T040 buildings) can replace its own contents without disturbing another's.
   *
   * T043, FR-023, contracts/solar-scene.md — the dome is TWO sibling groups, each its own disposal
   * scope, rather than one. The fixed group holds what depends only on the site and the year; the
   * dated group holds what depends on the chosen date and instant. Keeping them apart is what lets
   * a date change dispose and rebuild the arcs while the compass dial — and its baked 2048²
   * texture — is left entirely untouched (FR-021, SC-008).
   */
  readonly sunPathFixedGroup = new THREE.Group()
  readonly sunPathDatedGroup = new THREE.Group()
  readonly buildingsGroup = new THREE.Group()
  readonly groundGroup = new THREE.Group()
  readonly lightGroup = new THREE.Group()

  private sunLight: SunLight | null = null
  private shadowGround: ShadowGround | null = null
  private showBuildingMass = false
  private currentRadiusMetres = NO_BUILDINGS_FALLBACK_EXTENT_METRES
  private contentExtentMetres = NO_BUILDINGS_FALLBACK_EXTENT_METRES
  private tallestBuildingMetres = 0
  /** The elevation the radius was last sized for, so `setContentBounds` can resize without waiting
   * for the next tick. Starts high: overhead sun, shortest shadows, tightest fit. */
  private lastAltitudeDegrees = 90
  /** T035 — the sun direction the light was last actually aimed at, and so the one the playback
   * gate measures against. `null` means "cannot be gated": either nothing has been drawn yet, or
   * the geometry changed and FR-019 requires the next update to go through. */
  private lastAppliedSunDirection: THREE.Vector3 | null = null
  private currentMarker: THREE.Mesh | null = null
  /** T044 — a `lat|lng|year` key for the FIXED furniture. It changes only on a site change or a
   * year change, which are the only two things permitted to rebuild it (FR-022). */
  private fixedFurnitureBuiltForKey: string | null = null
  /** T051, T044 — a `date|lat|lng` key for the DATED path; rebuilt only when this changes, never on
   * a time-of-day tick, which just moves the marker (FR-019, FR-021, FR-023, SC-004, research D10). */
  private datedPathBuiltForKey: string | null = null
  /** specs/076 — sized to enclose the building under study (`sunPathDomeRadiusFor`); part of both
   * rebuild keys, since every arc, the dial and the shell are drawn at it. */
  private domeRadiusMetres = SUN_PATH_DOME_RADIUS_METRES
  /** The last `updateSunPath` arguments, so a new site building can resize the dome at once rather
   * than on the next tick — the buildings and the clock are driven by separate effects. */
  private lastSunPathArguments: Parameters<SolarScene['updateSunPath']> | null = null

  constructor(drawingSpace: DrawingSpaceHandle) {
    this.drawingSpace = drawingSpace
    drawingSpace.group.add(this.sunPathFixedGroup, this.sunPathDatedGroup, this.buildingsGroup, this.groundGroup, this.lightGroup)
  }

  /**
   * T023/T024, US2, FR-008, FR-013 — records what the footprints actually occupy, and (re)builds
   * the light and ground plane around it. Called when the buildings or their heights change, never
   * on a time-of-day tick.
   *
   * The radius itself is no longer an argument: it is *derived* from these bounds and the sun's
   * current elevation, in `aimSun`, so there is still exactly one radius (FR-009) but it is a
   * function of content rather than of the radius the buildings were queried with. An empty site
   * gets the stated fallback extent and nothing casting.
   */
  setContentBounds(extentMetres: number, tallestBuildingMetres: number): void {
    this.contentExtentMetres = extentMetres > 0 ? extentMetres : NO_BUILDINGS_FALLBACK_EXTENT_METRES
    this.tallestBuildingMetres = tallestBuildingMetres
    this.applyShadowRadius(this.lastAltitudeDegrees)
    this.clearShadowGate()
  }

  /**
   * T035, T036, FR-018, FR-019, FR-020, research D8 — moves the light direction and resizes the
   * one radius to the new elevation, and reports whether anything actually moved.
   *
   * During continuous playback ONLY, a sun movement below `SHADOW_GATE_DEGREES` is skipped
   * entirely and `false` is returned, so the caller withholds its `invalidate()` and no frame —
   * and therefore no shadow-map pass — is drawn for it. This is the whole gate: nothing here
   * touches `renderer.shadowMap`, `scene.environment` or any other renderer-global state, which
   * FR-024 and the viewer's own constraints forbid this feature from owning.
   *
   * Scrubbing and single-step time changes pass `isContinuousPlayback: false` and are never
   * gated (FR-020): a deliberate user action must always produce a frame.
   */
  aimSun(azimuthDegrees: number, altitudeDegrees: number, isContinuousPlayback = false): boolean {
    const enu = solarPositionToEnuUnitVector(azimuthDegrees, altitudeDegrees)
    const direction = new THREE.Vector3(enu.x, enu.y, enu.z)

    if (isContinuousPlayback && this.lastAppliedSunDirection) {
      const dot = Math.min(1, Math.max(-1, this.lastAppliedSunDirection.dot(direction)))
      if ((Math.acos(dot) * 180) / Math.PI < SHADOW_GATE_DEGREES) return false
    }

    this.lastAltitudeDegrees = altitudeDegrees
    this.lastAppliedSunDirection = direction
    this.applyShadowRadius(altitudeDegrees)
    this.sunLight?.aimAt(azimuthDegrees, altitudeDegrees)
    this.shadowGround?.setVisible(altitudeDegrees > MIN_SHADOW_ELEVATION_DEGREES)
    return true
  }

  /** FR-019 — any geometry change must update shadows immediately, however little the sun has
   * moved. Forgetting the last applied direction is what guarantees the next `aimSun` cannot be
   * gated away. */
  private clearShadowGate(): void {
    this.lastAppliedSunDirection = null
  }

  /** The single radius of FR-009, computed in one place and handed to both consumers. */
  private applyShadowRadius(altitudeDegrees: number): void {
    const radius = shadowRadiusMetres(this.contentExtentMetres, this.tallestBuildingMetres, altitudeDegrees)
    this.currentRadiusMetres = radius

    if (!this.sunLight) {
      this.sunLight = new SunLight(radius, this.tallestBuildingMetres)
      this.sunLight.addTo(this.lightGroup)
    } else {
      this.sunLight.configureShadowCamera(radius, this.tallestBuildingMetres)
    }

    if (!this.shadowGround) {
      this.shadowGround = new ShadowGround(radius)
      this.shadowGround.addTo(this.groundGroup)
    } else {
      this.shadowGround.setRadius(radius)
    }
  }

  /**
   * T017, T042, T044, T051, FR-005…FR-008, FR-021, FR-022, FR-023, SC-004, SC-008 — updates the
   * dome, rebuilding as little of it as the change requires:
   *
   * - the fixed furniture (dial, mount post, shell, monthly lattice) only when the SITE or the
   *   YEAR changes;
   * - the dated path (day arc, seasonal extremes, hour marks, marker) only when the local DATE or
   *   the site changes;
   * - on every other call, nothing is rebuilt at all — the existing marker is repositioned in
   *   place, which is what keeps time-of-day scrubbing cheap.
   *
   * Returns whether anything was rebuilt. T035's playback gate decides whether the SUN moved
   * enough to redraw; new geometry has to be drawn regardless, so the caller needs to tell the two
   * cases apart (a date change near a solstice can move the sun by less than the gate while
   * replacing every arc in the group).
   */
  updateSunPath(localDate: string, latitude: number, longitude: number, currentInstantUtc: Date, azimuthDegrees: number, altitudeDegrees: number): boolean {
    this.lastSunPathArguments = [localDate, latitude, longitude, currentInstantUtc, azimuthDegrees, altitudeDegrees]
    const [year] = localDate.split('-').map(Number)
    const radius = this.domeRadiusMetres
    const site = `${latitude.toFixed(6)}|${longitude.toFixed(6)}`
    const fixedKey = `${site}|${year}|${radius}`
    const datedKey = `${localDate}|${site}|${radius}`
    let rebuilt = false

    if (this.fixedFurnitureBuiltForKey !== fixedKey) {
      this.disposeGroupContents(this.sunPathFixedGroup)
      const furniture = buildFixedFurniture(latitude, longitude, year, radius)
      // Furniture is added before the dated group's arcs in scene order so the lattice sits behind
      // them; the shell carries its own `renderOrder` because it must still draw last of all.
      this.sunPathFixedGroup.add(furniture.dial, furniture.mountPost, ...furniture.monthlyArcs, furniture.shell)
      this.fixedFurnitureBuiltForKey = fixedKey
      rebuilt = true
    }

    if (this.datedPathBuiltForKey !== datedKey || !this.currentMarker) {
      this.disposeGroupContents(this.sunPathDatedGroup)
      const dateForArc = (() => {
        const [y, month, day] = localDate.split('-').map(Number)
        return new Date(Date.UTC(y, month - 1, day))
      })()
      const dated = buildDatedPath(dateForArc, latitude, longitude, currentInstantUtc, radius)
      for (const object of [dated.chosenDay, dated.summerExtreme, dated.winterExtreme, ...dated.hourMarks, dated.currentPositionMarker]) {
        if (object) this.sunPathDatedGroup.add(object)
      }
      this.currentMarker = dated.currentPositionMarker
      this.datedPathBuiltForKey = datedKey
      rebuilt = true
    } else {
      updateCurrentPositionMarker(this.currentMarker, azimuthDegrees, altitudeDegrees, radius)
    }

    return rebuilt
  }

  /** T040, T042 — rebuilds the buildings group from a fresh footprint list. Rebuilt only when
   * buildings or heights change (FR-019), never on a time-of-day tick. T033/T023: the extent and
   * tallest height measured while building feed the shadow radius directly, so the caller never has
   * to walk the footprints a second time to size the rig. */
  rebuildBuildings(buildings: SiteBuildingDto[], showMass: boolean = this.showBuildingMass): FootprintBuildResult {
    this.showBuildingMass = showMass
    this.disposeGroupContents(this.buildingsGroup)
    const built = buildFootprintMeshes(buildings, showMass)
    if (built.mesh) this.buildingsGroup.add(built.mesh)
    this.setContentBounds(built.extentMetres, built.tallestHeightMetres)
    this.fitDomeTo(built.siteReachMetres)
    // T032, FR-028 — returned rather than swallowed: footprints dropped here are buildings the
    // user can see on the basemap but which cast no shadow, and the caller has to say so.
    return built
  }

  /** specs/076 — grows or shrinks the dome to enclose the building under study, rebuilding it now if
   * it has already been drawn. The shadow rig is untouched: it is sized by `setContentBounds`. */
  private fitDomeTo(siteReachMetres: number): void {
    const radius = sunPathDomeRadiusFor(siteReachMetres)
    if (radius === this.domeRadiusMetres) return
    this.domeRadiusMetres = radius
    if (this.lastSunPathArguments) this.updateSunPath(...this.lastSunPathArguments)
  }

  get sunPathDomeRadiusMetres(): number {
    return this.domeRadiusMetres
  }

  /** T041 — reveals the massing without rebuilding geometry; driven by the Building Corrections
   * panel's "Show building massing" switch. */
  setShowBuildingMass(showMass: boolean): void {
    this.showBuildingMass = showMass
    for (const child of this.buildingsGroup.children) {
      if (child instanceof THREE.Mesh) setShowMass(child, showMass)
    }
  }

  get radiusMetres(): number {
    return this.currentRadiusMetres
  }

  /** Traverses rather than walking direct children only: the dome's mount post is a `Group`, and a
   * shallow pass would leave its meshes' geometry and materials undisposed. Textures are released
   * too — the compass dial bakes a 2048² canvas texture that is rebuilt on every date change, so
   * leaking it would accumulate a few megabytes per scrub across a calendar. */
  private disposeGroupContents(group: THREE.Group): void {
    for (const child of [...group.children]) {
      group.remove(child)
      child.traverse((descendant) => {
        const mesh = descendant as THREE.Mesh
        mesh.geometry?.dispose()
        const material = mesh.material as THREE.Material | THREE.Material[] | undefined
        const materials = Array.isArray(material) ? material : material ? [material] : []
        for (const entry of materials) {
          for (const value of Object.values(entry)) {
            if (value && typeof value === 'object' && 'isTexture' in value) {
              ;(value as THREE.Texture).dispose()
            }
          }
          entry.dispose()
        }
      })
    }
  }

  /** FR-026, research D15 — moves the whole analysis (dome, buildings, ground plane, light
   * target) relative to the ground by translating the extension's own Drawing Space group in Z.
   * Never calls anything that sets the viewer's single reference point. */
  setGroundOffset(groundOffsetMetres: number): void {
    this.drawingSpace.group.position.z = groundOffsetMetres
    this.clearShadowGate() // FR-019 — a geometry change always redraws, however still the sun is.
  }

  invalidate(): void {
    this.drawingSpace.invalidate()
  }

  /** Test-only teardown — disposes every descendant's geometry/material/texture directly. In
   * production this is never called: `DrawingSpaceRegistry.release()` (framework-owned) already
   * disposes the whole group when the extension stops (FR-040), so a unit test that constructs a
   * `SolarScene` around a bare `THREE.Group` (no registry) needs its own way to clean up. */
  disposeAll(): void {
    for (const group of [this.sunPathFixedGroup, this.sunPathDatedGroup, this.buildingsGroup, this.groundGroup, this.lightGroup]) {
      group.traverse((child) => {
        const mesh = child as THREE.Mesh
        mesh.geometry?.dispose()
        const material = mesh.material as THREE.Material | THREE.Material[] | undefined
        if (Array.isArray(material)) {
          material.forEach((m) => m.dispose())
        } else {
          material?.dispose()
        }
      })
      group.clear()
    }
  }
}
