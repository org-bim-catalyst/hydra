import * as THREE from 'three'
import { solarPosition } from '../solar/solarPosition'

/** research D17, contracts/solar-extension.md — the sun-path dome's radius, following the
 * reference implementation's proven value. */
export const SUN_PATH_DOME_RADIUS_METRES = 120

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

export interface SunPathObjects {
  /** `null` only when the chosen day never rises above the horizon at all (deep polar night). */
  chosenDay: THREE.Mesh | null
  summerExtreme: THREE.Mesh | null
  winterExtreme: THREE.Mesh | null
  hourMarks: THREE.Sprite[]
  currentPositionMarker: THREE.Mesh
}

/** contracts/solar-extension.md, FR-005, FR-006, FR-007 — the chosen day's arc, the two seasonal
 * extremes (in distinguishable materials — FR-006) and hour marks along the chosen day, plus the
 * sun's current position marker. Each is returned as a distinct object rather than merged into one
 * mesh, so a caller (and `sunPathCurve.test.ts`) can tell them apart. No `scene.environment`, no
 * glass dome shell (research D17 — dropped, not reproduced). */
export function buildSunPath(
  dateUtc: Date,
  latitude: number,
  longitude: number,
  currentInstantUtc: Date,
  radiusMetres: number = SUN_PATH_DOME_RADIUS_METRES,
): SunPathObjects {
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
 * change (`buildSunPath`) while this function runs on every tick. */
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
