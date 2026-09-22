import * as THREE from 'three'
import type { DrawingSpaceHandle } from '../../../viewer/scene/DrawingSpaceRegistry'
import { buildFootprintMeshes, setShowMass } from '../buildings/footprintGeometry'
import type { SiteBuildingDto } from '../api/siteBuildingsApi'
import { ShadowGround } from './shadowGround'
import {
  MIN_SHADOW_ELEVATION_DEGREES,
  NO_BUILDINGS_FALLBACK_EXTENT_METRES,
  SunLight,
  shadowRadiusMetres,
} from './sunLight'
import { buildSunPath, updateCurrentPositionMarker } from './sunPathCurve'

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

  /** Sub-groups children are organized under, so each concern (T017 sun path, T037 light, T038
   * ground, T040 buildings) can replace its own contents without disturbing another's. */
  readonly sunPathGroup = new THREE.Group()
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
  private currentMarker: THREE.Mesh | null = null
  /** T051 — a `date|lat|lng` key identifying what the dome was last built for; the dome is
   * rebuilt only when this changes (a new date OR a new site), never on every time-of-day tick
   * (FR-019, FR-022, FR-023, SC-004, research D10). */
  private sunPathBuiltForKey: string | null = null

  constructor(drawingSpace: DrawingSpaceHandle) {
    this.drawingSpace = drawingSpace
    drawingSpace.group.add(this.sunPathGroup, this.buildingsGroup, this.groundGroup, this.lightGroup)
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
  }

  /** FR-016, FR-017, FR-019, FR-022, FR-023 — moves the light direction and resizes the one radius
   * to the new elevation. Both are arithmetic plus a scale assignment; no geometry is built, which
   * is what keeps time scrubbing cheap enough to read as continuous motion (SC-004). */
  aimSun(azimuthDegrees: number, altitudeDegrees: number): void {
    this.lastAltitudeDegrees = altitudeDegrees
    this.applyShadowRadius(altitudeDegrees)
    this.sunLight?.aimAt(azimuthDegrees, altitudeDegrees)
    this.shadowGround?.setVisible(altitudeDegrees > MIN_SHADOW_ELEVATION_DEGREES)
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

  /** T017, T051, FR-005…FR-008, FR-019, FR-022, FR-023, SC-004 — rebuilds the sun-path dome (the
   * chosen day's arc, the seasonal extremes, the hour marks) only when the LOCAL DATE changes;
   * every other call just repositions/recolors the existing current-position marker in place. This
   * is the mechanism that keeps time-of-day scrubbing cheap: nothing here depends on `instantUtc`
   * except the marker. */
  updateSunPath(localDate: string, latitude: number, longitude: number, currentInstantUtc: Date, azimuthDegrees: number, altitudeDegrees: number): void {
    const key = `${localDate}|${latitude.toFixed(6)}|${longitude.toFixed(6)}`
    if (this.sunPathBuiltForKey !== key || !this.currentMarker) {
      this.disposeGroupContents(this.sunPathGroup)
      const dateForArc = (() => {
        const [year, month, day] = localDate.split('-').map(Number)
        return new Date(Date.UTC(year, month - 1, day))
      })()
      const sunPath = buildSunPath(dateForArc, latitude, longitude, currentInstantUtc)
      const objects: (THREE.Object3D | null)[] = [
        // Furniture first so the dial and lattice sit behind the arcs in draw order.
        sunPath.dial,
        sunPath.mountPost,
        ...sunPath.monthlyArcs,
        sunPath.chosenDay,
        sunPath.summerExtreme,
        sunPath.winterExtreme,
        ...sunPath.hourMarks,
        sunPath.currentPositionMarker,
        // Transparent shell last: it writes no depth, so it must draw over what it encloses.
        sunPath.shell,
      ]
      for (const object of objects) {
        if (object) this.sunPathGroup.add(object)
      }
      this.currentMarker = sunPath.currentPositionMarker
      this.sunPathBuiltForKey = key
    } else {
      updateCurrentPositionMarker(this.currentMarker, azimuthDegrees, altitudeDegrees)
    }
  }

  /** T040, T042 — rebuilds the buildings group from a fresh footprint list. Rebuilt only when
   * buildings or heights change (FR-019), never on a time-of-day tick. T033/T023: the extent and
   * tallest height measured while building feed the shadow radius directly, so the caller never has
   * to walk the footprints a second time to size the rig. */
  rebuildBuildings(buildings: SiteBuildingDto[], showMass: boolean = this.showBuildingMass): void {
    this.showBuildingMass = showMass
    this.disposeGroupContents(this.buildingsGroup)
    const built = buildFootprintMeshes(buildings, showMass)
    for (const mesh of built.meshes) this.buildingsGroup.add(mesh)
    this.setContentBounds(built.extentMetres, built.tallestHeightMetres)
  }

  /** T041 — the developer-only toggle that reveals massing without rebuilding geometry. */
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
  }

  invalidate(): void {
    this.drawingSpace.invalidate()
  }

  /** Test-only teardown — disposes every descendant's geometry/material/texture directly. In
   * production this is never called: `DrawingSpaceRegistry.release()` (framework-owned) already
   * disposes the whole group when the extension stops (FR-040), so a unit test that constructs a
   * `SolarScene` around a bare `THREE.Group` (no registry) needs its own way to clean up. */
  disposeAll(): void {
    for (const group of [this.sunPathGroup, this.buildingsGroup, this.groundGroup, this.lightGroup]) {
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
