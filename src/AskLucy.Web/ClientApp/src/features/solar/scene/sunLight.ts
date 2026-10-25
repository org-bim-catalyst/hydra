import * as THREE from 'three'
import { solarPositionToEnuUnitVector } from '../solar/solarPosition'

/**
 * T022, research D7, FR-012 — the lowest sun elevation at which shadows are drawn at all.
 *
 * Shadow length goes as `height / tan(elevation)`, which diverges as elevation → 0: a 50 m building
 * casts 2.9 km of shadow at 1° and 5.7 km at 0.5°. A radius derived from that length diverges with
 * it, and a fixed 2048² shadow map spread over kilometres is exactly the mush this release exists
 * to remove. 1° is below the elevation at which shadow detail means anything and above the one at
 * which the radius misbehaves.
 *
 * This is user-visible: a narrow window just after sunrise and just before sunset in which the sun
 * is up and no shadows are drawn. `copy.lowSunNoShadowsNotice` states it (T026) — an absent result
 * with a stated reason is not a silent one.
 */
export const MIN_SHADOW_ELEVATION_DEGREES = 1.0

/**
 * FR-009, research D16 — the shadow camera frustum is this much larger than the shadow-receiving
 * ground plane, so the plane sits STRICTLY inside the frustum rather than exactly at its edge.
 * Sampling at the frustum edge clamps, and a clamped shadow-map lookup reads as *in shadow* across
 * ground that nothing shades — the "big fake grey blob" specs/052 recorded. One radius still goes
 * in and two sizes still come out; only the radius's derivation changed.
 */
export const SHADOW_FRUSTUM_MARGIN = 1.08

/**
 * FR-011, SC-005 — the shadow map's resolution.
 *
 * A shadow map is a grid over the light's view of the site, so each texel has a footprint on the
 * ground. At a low sun that footprint is stretched along the light direction by `1 / sin(elevation)`:
 * the same 2048² map that draws crisp edges at noon draws visible saw-teeth at 7°, where a
 * ~0.45 m texel covers ~3.4 m of ground. The map is therefore doubled while the sun is low and
 * released back when it is not, so the extra memory is paid only for the hours that need it.
 */
export const SHADOW_MAP_SIZE = 2048

/** The resolution used while the sun is below `LOW_SUN_ELEVATION_DEGREES`. */
export const SHADOW_MAP_SIZE_LOW_SUN = 4096

/** The resolution used while the sun is below `VERY_LOW_SUN_ELEVATION_DEGREES`, where the stretch
 * (`1/sin`) is past 5.7 and the 4096² map still leaves steps metres wide on the ground. */
export const SHADOW_MAP_SIZE_VERY_LOW_SUN = 8192

/** Below this elevation the map doubles. Twenty degrees is where the stretch factor (1/sin) is
 * still under 3, which the 2048² map covers without visible stepping. */
export const LOW_SUN_ELEVATION_DEGREES = 20

/** Below this elevation the map doubles again: at 10° a texel already covers 5.7× its size on the
 * ground, so a 4096² map over a kilometre-long shadow still steps by a metre or two (SC-005). The
 * extra memory — about 270 MB of shadow target — is paid only in that window. */
export const VERY_LOW_SUN_ELEVATION_DEGREES = 10

/** FR-013 — the stated extent used when the analysis area contains no buildings at all. Nothing
 * casts, so nothing has to be reached; this only has to give the ground plane a sensible size. */
export const NO_BUILDINGS_FALLBACK_EXTENT_METRES = 200

/**
 * FR-008, FR-014, T023, research D6 — the one radius both the shadow camera extent and the ground
 * plane are sized from, derived from the footprints actually present rather than from a fixed
 * multiple of the radius they were queried with.
 *
 * `extentMetres` is the ground the buildings occupy; `tallestHeightMetres / tan(elevation)` is the
 * longest shadow any of them can cast at that elevation. Their sum is the furthest a shadow tip can
 * land from the reference point, so fitting tightly to the buildings can never truncate one.
 *
 * **The elevation argument is the deliberate difference from tasks.md T023's literal formula**,
 * which evaluates the shadow term once at `MIN_SHADOW_ELEVATION_DEGREES` and holds it there all
 * day. Measured against a real site (Badr: ~200 m of low-rise, tallest 9 m) that static radius is
 * 716 m against today's 200 m — a frustum 3× wider over the same 2048² map, i.e. shadows three
 * times *blurrier* at every elevation, which fails FR-011 ("equal or better at every sun
 * elevation") and SC-005 outright. Evaluating at the current elevation, floored at the same 1°,
 * satisfies FR-014 exactly as written at the floor while spending the rest of the day's slack on
 * resolution: 209 m at 45°, 251 m at 10°. It remains a single scalar (FR-009) and carries no
 * azimuth dependence (FR-014a).
 */
export function shadowRadiusMetres(
  extentMetres: number,
  tallestHeightMetres: number,
  sunElevationDegrees: number,
): number {
  const elevation = Math.max(sunElevationDegrees, MIN_SHADOW_ELEVATION_DEGREES)
  return extentMetres + tallestHeightMetres / Math.tan((elevation * Math.PI) / 180)
}

/**
 * contracts/solar-extension.md, research D6, D16 — a `DirectionalLight` + `AmbientLight` pair
 * built once per activation, sized to the derived radius. Never touches
 * `renderer.shadowMap.enabled` or `.type` — those are the renderer-global settings FR-038 exists
 * to keep out of a capability's hands; shadows are turned on only by declaring the `shadows`
 * drawing requirement (T044), and this module leaves the shadow-map *algorithm* (`PCFSoftShadowMap`
 * in the reference implementation) at whatever the renderer's own default already is, since
 * setting it here would be exactly the class of violation FR-038 forbids.
 */
export class SunLight {
  readonly directionalLight: THREE.DirectionalLight
  readonly ambientLight: THREE.AmbientLight

  /** How far along the sun direction the light is placed. Grows with the radius (see
   * `configureShadowCamera`), so `aimAt` has to read it rather than a module constant. */
  private distanceMetres = 400

  /** The content box the shadow camera has to cover: `radiusMetres` horizontally either side of
   * the origin, and `tallestBuildingMetres` up. Re-fitted to its projection on every aim. */
  private radiusMetres = 200
  private tallestBuildingMetres = 50

  constructor(radiusMetres: number, tallestBuildingMetres: number = 50) {
    this.directionalLight = new THREE.DirectionalLight(0xfff2d0, 2.6)
    this.directionalLight.castShadow = true
    // The scene is ENU (x east, y north, z up), so the world's up is +Z. Giving the shadow camera
    // that up keeps its map aligned with the world instead of rolling with a horizontal axis.
    ;(this.directionalLight.shadow.camera as THREE.OrthographicCamera).up.set(0, 0, 1)
    this.directionalLight.shadow.mapSize.set(SHADOW_MAP_SIZE, SHADOW_MAP_SIZE)
    this.directionalLight.shadow.bias = -0.0006

    this.ambientLight = new THREE.AmbientLight(0xffffff, 0.7)

    this.configureShadowCamera(radiusMetres, tallestBuildingMetres)
  }

  /**
   * FR-009, FR-014 — sizes the orthographic shadow camera to the derived radius, plus the margin
   * that keeps the ground plane strictly inside it.
   *
   * `near`/`far` used to bracket only the tallest building, which silently clipped the far end of a
   * long shadow: the depth range that produced is ~120 m, while content spans `±radius` along the
   * light axis. They now bracket the whole shadow volume, by pushing the light far enough out that
   * every caster and every receiver lies between the light and `far`. That is why the light's
   * distance is derived here rather than fixed — a 716 m radius with a 400 m light distance would
   * put the near half of the site *behind* the light and drop its shadows entirely.
   */
  configureShadowCamera(radiusMetres: number, tallestBuildingMetres: number): void {
    const extent = radiusMetres * SHADOW_FRUSTUM_MARGIN
    // Every point of content lies within `radius + height` of the origin, so placing the light at
    // least that far out puts all of it in `(0, 2 × distance)` — comfortably inside `near`..`far`.
    this.distanceMetres = Math.max(400, radiusMetres + tallestBuildingMetres + 50)
    this.radiusMetres = extent
    this.tallestBuildingMetres = tallestBuildingMetres

    const camera = this.directionalLight.shadow.camera as THREE.OrthographicCamera
    camera.left = -extent
    camera.right = extent
    camera.top = extent
    camera.bottom = -extent
    camera.near = 1
    camera.far = 2 * this.distanceMetres
    camera.updateProjectionMatrix()
  }

  get lightDistanceMetres(): number {
    return this.distanceMetres
  }

  /**
   * FR-012, FR-016, FR-017 — aims the light along the sun's ENU direction, and disables it (no
   * shadows cast) at or below `MIN_SHADOW_ELEVATION_DEGREES`, by the same path that already
   * handled below-horizon. One threshold, one code path: the low-sun window is not a special case
   * bolted on beside the night case, it is the same case with a different number.
   */
  aimAt(azimuthDegrees: number, altitudeDegrees: number): void {
    const castsShadows = altitudeDegrees > MIN_SHADOW_ELEVATION_DEGREES
    this.directionalLight.visible = castsShadows
    this.directionalLight.intensity = castsShadows ? 2.6 : 0
    this.applyMapSize(
      !castsShadows
        ? SHADOW_MAP_SIZE
        : altitudeDegrees <= VERY_LOW_SUN_ELEVATION_DEGREES
          ? SHADOW_MAP_SIZE_VERY_LOW_SUN
          : altitudeDegrees <= LOW_SUN_ELEVATION_DEGREES
            ? SHADOW_MAP_SIZE_LOW_SUN
            : SHADOW_MAP_SIZE,
    )

    const direction = solarPositionToEnuUnitVector(azimuthDegrees, altitudeDegrees)
    this.directionalLight.position.set(
      direction.x * this.distanceMetres,
      direction.y * this.distanceMetres,
      direction.z * this.distanceMetres,
    )
    this.directionalLight.target.position.set(0, 0, 0)
    this.directionalLight.target.updateMatrixWorld()
    this.fitShadowCamera(new THREE.Vector3(direction.x, direction.y, direction.z))
  }

  /**
   * Fits the orthographic shadow camera to the content box's ACTUAL projection, instead of a
   * square of `radius` on every side.
   *
   * A shadow map is a grid over the light's view. The content (a `2 × radius`-wide, `tallest`-high
   * box of buildings and ground) projects onto that view as a shape far smaller than the square
   * the camera used to allocate: seen from a 9° sun it is about seven times thinner along the
   * light's own up axis, because the ground meets that axis at a glancing angle (`sin(elevation)`).
   * Allocating the square therefore spent most of the texels on empty space — which is why a low
   * sun drew metre-wide steps however large the map was.
   *
   * The camera's right/up axes are perpendicular to the light direction, so a world point's
   * coordinates in them are the same measured from the origin as from the light: the bounds below
   * are correct without knowing the light's distance.
   */
  private fitShadowCamera(sunDirection: THREE.Vector3): void {
    const camera = this.directionalLight.shadow.camera as THREE.OrthographicCamera
    const look = sunDirection.clone().negate()
    const right = new THREE.Vector3().crossVectors(camera.up, look)
    // A sun straight overhead makes `cross(up, look)` degenerate; fall back to a stable axis.
    if (right.lengthSq() < 1e-8) right.set(1, 0, 0)
    right.normalize()
    const upAxis = new THREE.Vector3().crossVectors(look, right).normalize()

    let minRight = Infinity
    let maxRight = -Infinity
    let minUp = Infinity
    let maxUp = -Infinity
    const corner = new THREE.Vector3()
    for (const signX of [-1, 1]) {
      for (const signY of [-1, 1]) {
        for (const height of [0, this.tallestBuildingMetres]) {
          corner.set(signX * this.radiusMetres, signY * this.radiusMetres, height)
          const alongRight = corner.dot(right)
          const alongUp = corner.dot(upAxis)
          minRight = Math.min(minRight, alongRight)
          maxRight = Math.max(maxRight, alongRight)
          minUp = Math.min(minUp, alongUp)
          maxUp = Math.max(maxUp, alongUp)
        }
      }
    }

    camera.left = minRight
    camera.right = maxRight
    camera.bottom = minUp
    camera.top = maxUp
    camera.updateProjectionMatrix()
  }

  /**
   * Applies a shadow-map resolution. Three allocates the target from `mapSize` when it first
   * renders the shadow pass, so a change after that only takes effect once the existing target is
   * released — hence the dispose-and-null rather than a `mapSize.set` alone.
   */
  private applyMapSize(sizePx: number): void {
    const shadow = this.directionalLight.shadow
    if (shadow.mapSize.x === sizePx) return

    shadow.mapSize.set(sizePx, sizePx)
    shadow.map?.dispose()
    shadow.map = null
  }

  addTo(group: THREE.Group): void {
    group.add(this.directionalLight, this.directionalLight.target, this.ambientLight)
  }

  dispose(): void {
    this.directionalLight.dispose()
  }
}
