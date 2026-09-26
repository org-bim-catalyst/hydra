import * as THREE from 'three'
import { solarPosition } from '../solar/solarPosition'

/** research D17, contracts/solar-extension.md — the sun-path dome's radius, following the
 * reference implementation's proven value. */
export const SUN_PATH_DOME_RADIUS_METRES = 120

/** specs/076 — how far past the building under study the dome's shell sits, and the step its
 * radius grows in, so a small height correction does not rebuild the dome for a few centimetres. */
const DOME_CLEARANCE = 1.15
const DOME_RADIUS_STEP_METRES = 10
/** A mis-flagged site footprint must not balloon the dome over the whole neighbourhood. */
export const MAX_SUN_PATH_DOME_RADIUS_METRES = 600

/**
 * specs/076 — the dome radius that encloses the building under study: never smaller than the
 * reference value, otherwise grown until its furthest roof corner (`siteReachMetres`, measured
 * from the dome's centre) sits inside the shell. Purely presentational: the dome draws where the
 * sun is, and the shadows are cast by the directional light, whose direction does not depend on
 * how far away the dome draws the sun.
 */
export function sunPathDomeRadiusFor(siteReachMetres: number): number {
  const needed = Math.ceil((siteReachMetres * DOME_CLEARANCE) / DOME_RADIUS_STEP_METRES) * DOME_RADIUS_STEP_METRES
  return Math.min(MAX_SUN_PATH_DOME_RADIUS_METRES, Math.max(SUN_PATH_DOME_RADIUS_METRES, needed))
}

/** ENU (X=East, Y=North, Z=Up) — specs/051's published local frame, which this feature draws in
 * directly with no remapping (data-model.md "Solar Position" — Derived). */
export function sphericalToVec(azimuthDegrees: number, altitudeDegrees: number, radiusMetres: number): THREE.Vector3 {
  const az = (azimuthDegrees * Math.PI) / 180
  const alt = (altitudeDegrees * Math.PI) / 180
  return new THREE.Vector3(
    radiusMetres * Math.cos(alt) * Math.sin(az), // east
    radiusMetres * Math.cos(alt) * Math.cos(az), // north
    radiusMetres * Math.sin(alt), // up
  )
}

/** Samples the sun's path across one UTC calendar date every 6 minutes, keeping only points above
 * the horizon — mirroring the reference implementation's proven sampling density. */
export function sampleDayArcPoints(dateUtc: Date, latitude: number, longitude: number, radiusMetres: number): THREE.Vector3[] {
  const points: THREE.Vector3[] = []
  const dayStart = Date.UTC(dateUtc.getUTCFullYear(), dateUtc.getUTCMonth(), dateUtc.getUTCDate())
  for (let minute = 0; minute <= 1440; minute += 6) {
    const position = solarPosition(new Date(dayStart + minute * 60_000), latitude, longitude)
    if (position.altitudeDegrees >= 0) {
      points.push(sphericalToVec(position.azimuthDegrees, position.altitudeDegrees, radiusMetres))
    }
  }
  return points
}

/** research D17 — WebGL renders plain `THREE.Line` at ~1px regardless of requested width, which
 * is why the reference implementation's dome read as "pale". Arcs are therefore solid tube
 * geometry, never `THREE.Line`. Returns `null` for fewer than 2 points (nothing to draw — e.g. a
 * polar-night day with no above-horizon samples at all), which callers treat as "no arc for this
 * track", not an error. */
export function buildTubeFromPoints(points: THREE.Vector3[], tubeRadius: number, color: number, opacity = 1): THREE.Mesh | null {
  if (points.length < 2) return null
  const curve = new THREE.CatmullRomCurve3(points, false)
  const tubularSegments = Math.max(16, Math.min(400, points.length * 2))
  const geometry = new THREE.TubeGeometry(curve, tubularSegments, tubeRadius, 6, false)
  const material = new THREE.MeshBasicMaterial({ color, transparent: opacity < 1, opacity, depthWrite: opacity >= 1 })
  return new THREE.Mesh(geometry, material)
}

function makeHourMarkSprite(hourLabel: string): THREE.Sprite {
  const canvas = document.createElement('canvas')
  canvas.width = 256
  canvas.height = 128
  const ctx = canvas.getContext('2d')
  if (ctx) {
    ctx.fillStyle = '#ff5a3c'
    ctx.beginPath()
    if (typeof ctx.roundRect === 'function') ctx.roundRect(4, 24, 248, 80, 28)
    else ctx.rect(4, 24, 248, 80)
    ctx.fill()
    ctx.font = '700 46px Inter, sans-serif'
    ctx.fillStyle = '#ffffff'
    ctx.textAlign = 'center'
    ctx.textBaseline = 'middle'
    ctx.fillText(hourLabel, 128, 64)
  }
  const sprite = new THREE.Sprite(
    new THREE.SpriteMaterial({ map: new THREE.CanvasTexture(canvas), depthTest: false, transparent: true }),
  )
  sprite.scale.set(10.5, 6, 1)
  return sprite
}

/** data-model.md "Sun Path" — which calendar month is the summer/winter extreme depends on
 * hemisphere (FR-006: "the seasonal extremes for this hemisphere"). Northern hemisphere: June
 * solstice is summer. Southern hemisphere: December solstice is summer. */
function seasonalExtremeDates(year: number, latitude: number): { summer: Date; winter: Date } {
  const juneSolstice = new Date(Date.UTC(year, 5, 21))
  const decemberSolstice = new Date(Date.UTC(year, 11, 21))
  return latitude >= 0 ? { summer: juneSolstice, winter: decemberSolstice } : { summer: decemberSolstice, winter: juneSolstice }
}

/** The engraved compass dial the dome sits on — degree ticks every 5°, numbers every 30°, and
 * cardinal/intercardinal letters, baked into one canvas texture. This is not decoration: FR-005
 * requires the path be "correctly oriented against north", and a dial is what makes that legible
 * rather than asserted.
 *
 * Every stroke and glyph is drawn twice — a light halo behind a dark core — so the dial stays
 * readable over roads, parks or buildings in either map theme, the same "casing" trick real maps
 * use for trail markers. Reproduced from the reference implementation, which arrived at it because
 * a single-colour dial disappeared against the basemap. */
function makeCompassDialTexture(size = 2048): THREE.CanvasTexture {
  const canvas = document.createElement('canvas')
  canvas.width = canvas.height = size
  const ctx = canvas.getContext('2d')
  const texture = new THREE.CanvasTexture(canvas)
  if (!ctx) return texture

  const centre = size / 2
  const ringRadius = size * 0.42
  const ink = 'rgba(15,18,24,0.95)'
  const halo = 'rgba(255,255,255,0.92)'

  function casedStroke(draw: () => void, coreWidth: number): void {
    ctx!.lineWidth = coreWidth * 2.6
    ctx!.strokeStyle = halo
    draw()
    ctx!.lineWidth = coreWidth
    ctx!.strokeStyle = ink
    draw()
  }

  function outlinedText(text: string, x: number, y: number, font: string): void {
    ctx!.font = font
    ctx!.textAlign = 'center'
    ctx!.textBaseline = 'middle'
    ctx!.lineWidth = size * 0.009
    ctx!.strokeStyle = halo
    ctx!.strokeText(text, x, y)
    ctx!.fillStyle = ink
    ctx!.fillText(text, x, y)
  }

  casedStroke(() => {
    ctx.beginPath()
    ctx.arc(centre, centre, ringRadius, 0, Math.PI * 2)
    ctx.stroke()
  }, size * 0.0026)

  for (let degrees = 0; degrees < 360; degrees += 5) {
    const isMajor = degrees % 30 === 0
    const isMid = !isMajor && degrees % 10 === 0
    // 0° = North at the texture's top, increasing clockwise — matching compass convention.
    const angle = ((degrees - 90) * Math.PI) / 180
    const tickLength = isMajor ? size * 0.026 : isMid ? size * 0.017 : size * 0.009
    const outerRadius = ringRadius + tickLength
    casedStroke(() => {
      ctx.beginPath()
      ctx.moveTo(centre + Math.cos(angle) * ringRadius, centre + Math.sin(angle) * ringRadius)
      ctx.lineTo(centre + Math.cos(angle) * outerRadius, centre + Math.sin(angle) * outerRadius)
      ctx.stroke()
    }, isMajor ? size * 0.0026 : size * 0.0015)

    if (isMajor) {
      const labelRadius = outerRadius + size * 0.028
      outlinedText(
        String(degrees),
        centre + Math.cos(angle) * labelRadius,
        centre + Math.sin(angle) * labelRadius,
        `500 ${Math.round(size * 0.018)}px Inter, sans-serif`,
      )
    }
  }

  const cardinals = [
    { angle: 0, label: 'N' }, { angle: 45, label: 'NE' }, { angle: 90, label: 'E' }, { angle: 135, label: 'SE' },
    { angle: 180, label: 'S' }, { angle: 225, label: 'SW' }, { angle: 270, label: 'W' }, { angle: 315, label: 'NW' },
  ]
  for (const cardinal of cardinals) {
    const isPrimary = cardinal.label.length === 1
    const angle = ((cardinal.angle - 90) * Math.PI) / 180
    const labelRadius = ringRadius + size * (isPrimary ? 0.062 : 0.053)
    outlinedText(
      cardinal.label,
      centre + Math.cos(angle) * labelRadius,
      centre + Math.sin(angle) * labelRadius,
      `${isPrimary ? 700 : 600} ${Math.round(size * (isPrimary ? 0.03 : 0.021))}px Inter, sans-serif`,
    )
  }

  texture.anisotropy = 4
  texture.needsUpdate = true
  return texture
}

/** The dial platform the whole instrument reads as sitting on. `0.84` is twice the texture's own
 * ring-radius fraction (0.42), so the drawn ring lands exactly at `radiusMetres`. */
export function buildCompassDial(radiusMetres: number): THREE.Mesh {
  const mesh = new THREE.Mesh(
    new THREE.CircleGeometry(radiusMetres / 0.84, 96),
    new THREE.MeshStandardMaterial({
      map: makeCompassDialTexture(),
      transparent: true,
      alphaTest: 0.04,
      depthWrite: false,
      roughness: 0.7,
      metalness: 0.05,
    }),
  )
  // Just clear of z=0 so it never z-fights the shadow ground plane sharing that height.
  mesh.position.z = 0.05
  mesh.receiveShadow = true
  return mesh
}

/** The instrument's centre mounting post — a visual anchor tying the dome to the site point. */
export function buildMountPost(heightMetres = 12, radiusMetres = 1.1): THREE.Group {
  const group = new THREE.Group()
  const material = new THREE.MeshStandardMaterial({ color: 0x3a4152, metalness: 0.65, roughness: 0.3 })

  const post = new THREE.Mesh(new THREE.CylinderGeometry(radiusMetres * 0.5, radiusMetres * 0.75, heightMetres, 14), material)
  // CylinderGeometry is Y-aligned by default; this scene's up axis is Z (ENU).
  post.rotation.x = Math.PI / 2
  post.position.z = heightMetres / 2
  post.castShadow = true
  group.add(post)

  const knob = new THREE.Mesh(new THREE.SphereGeometry(radiusMetres * 1.3, 16, 16), material)
  knob.position.z = heightMetres
  knob.castShadow = true
  group.add(knob)

  return group
}

/** The translucent envelope enclosing the whole sun-path dome.
 *
 * research D17 dropped the reference implementation's glass shell because it used
 * `MeshPhysicalMaterial`'s `transmission`, which only reads as glass against a `scene.environment`
 * — and the scene is shared, framework-owned state an extension may not assign (FR-038). This is
 * the same envelope built from a plain transparent material instead: no environment map, no global
 * state touched, and it still gives the dome its enclosing volume. */
export function buildDomeShell(radiusMetres: number): THREE.Mesh {
  const mesh = new THREE.Mesh(
    new THREE.SphereGeometry(radiusMetres, 48, 24, 0, Math.PI * 2, 0, Math.PI / 2),
    new THREE.MeshStandardMaterial({
      color: 0x8fd6bc,
      transparent: true,
      opacity: 0.16,
      roughness: 0.12,
      metalness: 0.0,
      side: THREE.DoubleSide,
      depthWrite: false,
    }),
  )
  // SphereGeometry's hemisphere opens along +Y; rotate so it opens along this scene's +Z up axis.
  mesh.rotation.x = Math.PI / 2
  return mesh
}

/** The faint monthly lattice that gives the dome its characteristic woven look — the sun's arc on
 * the 15th of each month. Context for the chosen day and the two extremes, deliberately dim so it
 * never competes with them (FR-006 requires the extremes stay distinguishable). */
export function buildMonthlyGridArcs(latitude: number, longitude: number, year: number, radiusMetres: number): THREE.Mesh[] {
  const arcs: THREE.Mesh[] = []
  for (let month = 0; month < 12; month++) {
    const points = sampleDayArcPoints(new Date(Date.UTC(year, month, 15)), latitude, longitude, radiusMetres)
    const tube = buildTubeFromPoints(points, 0.3, 0xe8a03d, 0.4)
    if (tube) arcs.push(tube)
  }
  return arcs
}

/**
 * T042, FR-021, FR-022, contracts/solar-scene.md — the dome's FIXED FURNITURE: everything whose
 * shape depends only on where you are and which year it is, and which must therefore not so much
 * as flicker when the date changes.
 */
export interface FixedFurnitureObjects {
  dial: THREE.Mesh
  mountPost: THREE.Group
  shell: THREE.Mesh
  monthlyArcs: THREE.Mesh[]
}

/**
 * T042, FR-021 — the DATED PATH: everything that depends on the chosen date or the current
 * instant, and is therefore the only thing a date change is allowed to rebuild.
 */
export interface DatedPathObjects {
  /** `null` only when the chosen day never rises above the horizon at all (deep polar night). */
  chosenDay: THREE.Mesh | null
  summerExtreme: THREE.Mesh | null
  winterExtreme: THREE.Mesh | null
  hourMarks: THREE.Sprite[]
  currentPositionMarker: THREE.Mesh
}

/**
 * T042, FR-022, contracts/solar-scene.md — builds the compass dial, the mount post, the enclosing
 * shell and the monthly lattice. Depends on latitude, longitude, year and radius; deliberately NOT
 * on the date or the instant, which is the whole point of the split (FR-021, SC-008).
 *
 * The year is a parameter rather than being read off a date so the caller's own rebuild key and
 * this function's dependencies cannot drift apart: what the key says the furniture was built for
 * is exactly what was passed in.
 */
export function buildFixedFurniture(
  latitude: number,
  longitude: number,
  year: number,
  radiusMetres: number = SUN_PATH_DOME_RADIUS_METRES,
): FixedFurnitureObjects {
  const shell = buildDomeShell(radiusMetres)
  // The shell writes no depth and encloses everything else, so it has to draw last or the arcs
  // inside it come out tinted by nothing. It used to get that ordering for free by being appended
  // last to a single group; now that it lives in the fixed group and the arcs live in a sibling
  // group, the ordering has to be stated rather than inherited from assembly order.
  shell.renderOrder = 1

  return {
    dial: buildCompassDial(radiusMetres),
    mountPost: buildMountPost(),
    shell,
    monthlyArcs: buildMonthlyGridArcs(latitude, longitude, year, radiusMetres),
  }
}

/**
 * T042, FR-005, FR-006, FR-007, FR-021 — the chosen day's arc, the two seasonal extremes (in
 * distinguishable materials — FR-006), the hour marks along the chosen day, and the sun's current
 * position marker. Each is returned as a distinct object rather than merged into one mesh, so a
 * caller (and `sunPathCurve.test.ts`) can tell them apart.
 *
 * Replaces the fixed-plus-dated `buildSunPath` bag: rebuilding this no longer drags the compass
 * dial's 2048² baked texture and the twelve monthly arcs along with it.
 */
export function buildDatedPath(
  dateUtc: Date,
  latitude: number,
  longitude: number,
  currentInstantUtc: Date,
  radiusMetres: number = SUN_PATH_DOME_RADIUS_METRES,
): DatedPathObjects {
  const chosenDayPoints = sampleDayArcPoints(dateUtc, latitude, longitude, radiusMetres)
  const chosenDay = buildTubeFromPoints(chosenDayPoints, 0.7, 0xff5a3c, 1)

  const { summer, winter } = seasonalExtremeDates(dateUtc.getUTCFullYear(), latitude)
  const summerExtreme = buildTubeFromPoints(sampleDayArcPoints(summer, latitude, longitude, radiusMetres), 0.5, 0xffb020, 0.8)
  const winterExtreme = buildTubeFromPoints(sampleDayArcPoints(winter, latitude, longitude, radiusMetres), 0.5, 0x4fb0ff, 0.8)

  const hourMarks: THREE.Sprite[] = []
  const dayStart = Date.UTC(dateUtc.getUTCFullYear(), dateUtc.getUTCMonth(), dateUtc.getUTCDate())
  for (let hour = 0; hour < 24; hour++) {
    const position = solarPosition(new Date(dayStart + hour * 3_600_000), latitude, longitude)
    if (position.altitudeDegrees >= 2) {
      const sprite = makeHourMarkSprite(String(hour).padStart(2, '0'))
      sprite.position.copy(sphericalToVec(position.azimuthDegrees, position.altitudeDegrees, radiusMetres))
      hourMarks.push(sprite)
    }
  }

  const currentPosition = solarPosition(currentInstantUtc, latitude, longitude)
  const clampedAltitude = Math.max(currentPosition.altitudeDegrees, -6)
  const currentPositionMarker = new THREE.Mesh(
    new THREE.SphereGeometry(6, 24, 24),
    new THREE.MeshBasicMaterial({ color: currentPosition.altitudeDegrees >= 0 ? 0xffd700 : 0x555b6e }),
  )
  currentPositionMarker.position.copy(sphericalToVec(currentPosition.azimuthDegrees, clampedAltitude, radiusMetres))

  return { chosenDay, summerExtreme, winterExtreme, hourMarks, currentPositionMarker }
}

/** T051, FR-019, FR-022, FR-023, SC-004 — repositions and recolors an EXISTING current-position
 * marker in place, without touching the dome's tube geometry. This is what makes scrubbing cheap
 * enough to read as continuous motion: the chosen day's arc, the seasonal extremes and the hour
 * marks depend only on the calendar date, not the time of day, so they are rebuilt once per date
 * change (`buildDatedPath`) while this function runs on every tick. */
export function updateCurrentPositionMarker(
  marker: THREE.Mesh,
  azimuthDegrees: number,
  altitudeDegrees: number,
  radiusMetres: number = SUN_PATH_DOME_RADIUS_METRES,
): void {
  const clampedAltitude = Math.max(altitudeDegrees, -6)
  marker.position.copy(sphericalToVec(azimuthDegrees, clampedAltitude, radiusMetres))
  ;(marker.material as THREE.MeshBasicMaterial).color.set(altitudeDegrees >= 0 ? 0xffd700 : 0x555b6e)
}
