import * as THREE from 'three'
import type { DrawingSpaceHandle } from '../../../viewer/scene/DrawingSpaceRegistry'
import { buildFootprintMesh, setShowMass } from '../buildings/footprintGeometry'
import type { SiteBuildingDto } from '../api/siteBuildingsApi'
import { ShadowGround } from './shadowGround'
import { SunLight } from './sunLight'
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
  private currentRadiusMetres = 200
  private currentMarker: THREE.Mesh | null = null
  /** T051 — a `date|lat|lng` key identifying what the dome was last built for; the dome is
   * rebuilt only when this changes (a new date OR a new site), never on every time-of-day tick
   * (FR-019, FR-022, FR-023, SC-004, research D10). */
  private sunPathBuiltForKey: string | null = null

  constructor(drawingSpace: DrawingSpaceHandle) {
    this.drawingSpace = drawingSpace
    drawingSpace.group.add(this.sunPathGroup, this.buildingsGroup, this.groundGroup, this.lightGroup)
  }

  /** T037/T038, US2 — (re)builds the light and ground plane sized to `radiusMetres`, per research
   * D16's single-radius rule. Called once buildings are known (or the radius otherwise changes) —
   * never on every time-of-day tick (T051, FR-019/FR-023: only the light's aim moves on scrub). */
  ensureShadowRig(radiusMetres: number, tallestBuildingMetres: number): void {
    this.currentRadiusMetres = radiusMetres
    if (!this.sunLight) {
      this.sunLight = new SunLight(radiusMetres, tallestBuildingMetres)
      this.sunLight.addTo(this.lightGroup)
    } else {
      this.sunLight.configureShadowCamera(radiusMetres, tallestBuildingMetres)
    }
    // Rebuilt (not just left in place) when the radius changes, for the same single-radius reason
    // the frustum is reconfigured above: the light's frustum tracks `radiusMetres` but the ground
    // plane's geometry is fixed at construction, so leaving a stale plane behind after a SMALLER
    // radius would put the plane outside the new, tighter frustum — research D16's clamped-lookup
    // grey blob, arrived at from the opposite direction.
    if (!this.shadowGround) {
      this.shadowGround = new ShadowGround(radiusMetres)
      this.shadowGround.addTo(this.groundGroup)
    } else if (this.shadowGround.radiusMetres !== radiusMetres) {
      const wasVisible = this.shadowGround.mesh.visible
      this.shadowGround.dispose()
      this.groundGroup.remove(this.shadowGround.mesh)
      this.shadowGround = new ShadowGround(radiusMetres)
      this.shadowGround.setVisible(wasVisible)
      this.shadowGround.addTo(this.groundGroup)
    }
  }

  /** FR-016, FR-017, FR-019, FR-022, FR-023 — moves only the light direction and the ground
   * plane's visibility; never rebuilds geometry. This is what keeps time scrubbing cheap enough to
   * read as continuous motion (SC-004). */
  aimSun(azimuthDegrees: number, altitudeDegrees: number): void {
    this.sunLight?.aimAt(azimuthDegrees, altitudeDegrees)
    this.shadowGround?.setVisible(altitudeDegrees > 0)
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
      for (const object of [sunPath.chosenDay, sunPath.summerExtreme, sunPath.winterExtreme, ...sunPath.hourMarks, sunPath.currentPositionMarker]) {
        if (object) this.sunPathGroup.add(object)
      }
      this.currentMarker = sunPath.currentPositionMarker
      this.sunPathBuiltForKey = key
    } else {
      updateCurrentPositionMarker(this.currentMarker, azimuthDegrees, altitudeDegrees)
    }
  }

  /** T040, T042 — rebuilds the buildings group from a fresh footprint list. Rebuilt only when
   * buildings or heights change (FR-019), never on a time-of-day tick. */
  rebuildBuildings(buildings: SiteBuildingDto[], showMass: boolean = this.showBuildingMass): void {
    this.showBuildingMass = showMass
    this.disposeGroupContents(this.buildingsGroup)
    for (const building of buildings) {
      const mesh = buildFootprintMesh(building, showMass)
      if (mesh) this.buildingsGroup.add(mesh)
    }
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

  private disposeGroupContents(group: THREE.Group): void {
    for (const child of [...group.children]) {
      group.remove(child)
      const mesh = child as THREE.Mesh
      mesh.geometry?.dispose()
      const material = mesh.material as THREE.Material | THREE.Material[] | undefined
      if (Array.isArray(material)) material.forEach((m) => m.dispose())
      else material?.dispose()
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
