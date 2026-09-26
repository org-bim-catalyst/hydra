import * as THREE from 'three'
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js'
import { worldToLocal } from '../../../viewer/api/coordinateFrame'
import type { SiteBuildingDto } from '../api/siteBuildingsApi'

/** research D13 — the site building is tinted differently from its neighbours so the massing
 * toggle can distinguish it. Since T031 every footprint shares one material, so the distinction
 * is carried per-vertex rather than per-material. */
const SITE_BUILDING_COLOUR = 0xffcf8a
const NEIGHBOUR_COLOUR = 0x9aa4b8

/**
 * T031, FR-015 — one footprint's extrusion, as bare geometry. Returns `null` for a ring too
 * degenerate to extrude; the caller counts those rather than dropping them silently (FR-028).
 *
 * Deliberately no mesh and no material: every footprint is merged into a single mesh by
 * `buildFootprintMeshes`, so the per-building draw call this used to create no longer exists.
 */
export function buildFootprintGeometry(building: SiteBuildingDto): THREE.ExtrudeGeometry | null {
  const localPoints = building.ring.map((p) => worldToLocal(p, 0))
  // Drop a duplicated closing vertex if present — THREE.Shape closes its own path.
  const points = localPoints.length > 1 &&
    Math.abs(localPoints[0].x - localPoints[localPoints.length - 1].x) < 1e-6 &&
    Math.abs(localPoints[0].y - localPoints[localPoints.length - 1].y) < 1e-6
    ? localPoints.slice(0, -1)
    : localPoints

  if (points.length < 3) return null

  const shape = new THREE.Shape(points.map((p) => new THREE.Vector2(p.x, p.y)))
  const geometry = new THREE.ExtrudeGeometry(shape, { depth: building.heightMetres, bevelEnabled: false })

  // `mergeGeometries` requires every input to carry the same attributes, so the colour is written
  // here rather than only on the buildings that need tinting.
  const colour = new THREE.Color(building.isSiteBuilding ? SITE_BUILDING_COLOUR : NEIGHBOUR_COLOUR)
  const vertexCount = geometry.attributes.position.count
  const colours = new Float32Array(vertexCount * 3)
  for (let i = 0; i < vertexCount; i++) {
    colours[i * 3] = colour.r
    colours[i * 3 + 1] = colour.g
    colours[i * 3 + 2] = colour.b
  }
  geometry.setAttribute('color', new THREE.BufferAttribute(colours, 3))

  return geometry
}

/**
 * research D13 — building meshes cast shadows without being drawn: the basemap already draws its
 * own 3D buildings, and a second copy from a different data source reads as a shifted "ghost
 * duplicate" rather than information. `colorWrite: false` / `depthWrite: false` removes the mesh
 * from the colour and depth passes while `castShadow: true` keeps it in the separate shadow-map
 * depth pass, so it casts a real shadow while drawing nothing. `showMass` is a developer-only
 * toggle that flips `colorWrite` back on to verify the massing (FR-016).
 */
export function buildFootprintMaterial(showMass: boolean): THREE.MeshStandardMaterial {
  return new THREE.MeshStandardMaterial({
    vertexColors: true,
    roughness: 0.9,
    metalness: 0.05,
    colorWrite: showMass,
    depthWrite: showMass,
  })
}

/** What the shadow rig needs to know about the footprints, measured while they are built rather
 * than by a second pass over the source data (T033). */
export interface FootprintBuildResult {
  /**
   * T031, FR-015 — every usable footprint merged into ONE mesh, so drawing cost stops scaling
   * with building count. `null` when nothing usable was supplied.
   */
  mesh: THREE.Mesh | null
  /** Source ids in merge order, so a caller can relate the merged geometry back to its inputs. */
  buildingIds: string[]
  /**
   * The greatest horizontal distance from the scene's reference point to any footprint vertex —
   * the ground the buildings actually occupy, as opposed to the radius they were *queried* with.
   * The two are the same only when the buildings happen to fill the query circle; a tight cluster
   * or a lone tower occupies far less, which is the slack T023 spends on shadow-map resolution.
   */
  extentMetres: number
  /** Tallest resolved height among the usable footprints — the multiplier on shadow length. */
  tallestHeightMetres: number
  /**
   * specs/076 — the straight-line distance from the reference point to the furthest roof corner of
   * the building under study: the site building plus any taller part the server split off it
   * (`{siteId}_part…`, HeightEnrichingBuildingFootprintProvider). When the site has a resolved
   * boundary, every footprint whose centre lies inside it counts too, and so does the boundary
   * itself: BurJuman is two footprints (and a tower) the boundary resolver already merges into one
   * site, but only one of them is flagged. What the sun-path dome has to enclose; 0 when there is
   * neither a site building nor a boundary.
   */
  siteReachMetres: number
  /** Footprints whose ring was too degenerate to extrude. Counted rather than dropped silently. */
  excludedCount: number
}

function isPartOfSite(buildingId: string, siteIds: string[]): boolean {
  return siteIds.some((siteId) => buildingId === siteId || buildingId.startsWith(`${siteId}_part`))
}

type LocalPoint = { x: number; y: number }

/** Even-odd ray cast, in the scene's local metres. */
function isInside(point: LocalPoint, ring: LocalPoint[]): boolean {
  let inside = false
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
    const a = ring[i]
    const b = ring[j]
    if ((a.y > point.y) !== (b.y > point.y) && point.x < ((b.x - a.x) * (point.y - a.y)) / (b.y - a.y) + a.x) {
      inside = !inside
    }
  }
  return inside
}

function centreOf(ring: SiteBuildingDto['ring']): LocalPoint {
  const local = ring.map((point) => worldToLocal(point, 0))
  return {
    x: local.reduce((sum, p) => sum + p.x, 0) / local.length,
    y: local.reduce((sum, p) => sum + p.y, 0) / local.length,
  }
}

/**
 * T031, T032, T033 — builds every footprint, merges them into a single mesh, and reports the
 * extent, the tallest height and the exclusions in the same pass, so neither the shadow radius
 * (T023) nor the exclusion notice (FR-028) has to re-traverse the source data.
 *
 * Returning zero extent/height on an empty list is deliberate: the caller applies the stated
 * no-buildings fallback (FR-013) rather than this module inventing one.
 */
export function buildFootprintMeshes(
  buildings: SiteBuildingDto[],
  showMass: boolean,
  siteRings: SiteBuildingDto['ring'][] = [],
): FootprintBuildResult {
  const geometries: THREE.ExtrudeGeometry[] = []
  const buildingIds: string[] = []
  let extentMetres = 0
  let tallestHeightMetres = 0
  let siteReachMetres = 0
  let excludedCount = 0
  // specs/077 — a site can be several outlines (a mall and the same-named tower across the street).
  const boundaries = siteRings.filter((ring) => ring.length >= 3).map((ring) => ring.map((point) => worldToLocal(point, 0)))
  const siteIds = buildings
    .filter((b) => b.isSiteBuilding || (b.ring.length > 0 && boundaries.some((boundary) => isInside(centreOf(b.ring), boundary))))
    .map((b) => b.id)
  for (const corner of boundaries.flat()) siteReachMetres = Math.max(siteReachMetres, Math.hypot(corner.x, corner.y))

  for (const building of buildings) {
    const geometry = buildFootprintGeometry(building)
    if (!geometry) {
      excludedCount += 1
      continue
    }
    geometries.push(geometry)
    buildingIds.push(building.id)
    tallestHeightMetres = Math.max(tallestHeightMetres, building.heightMetres)
    const partOfSite = isPartOfSite(building.id, siteIds)
    for (const point of building.ring) {
      const local = worldToLocal(point, 0)
      const distance = Math.hypot(local.x, local.y)
      extentMetres = Math.max(extentMetres, distance)
      if (partOfSite) siteReachMetres = Math.max(siteReachMetres, Math.hypot(distance, building.heightMetres))
    }
  }

  if (geometries.length === 0) {
    return { mesh: null, buildingIds, extentMetres, tallestHeightMetres, siteReachMetres, excludedCount }
  }

  // `useGroups: false` — one group would reintroduce one draw call per building, which is exactly
  // the cost FR-015 exists to remove. The per-building tint survives as a vertex attribute.
  const merged = mergeGeometries(geometries, false)
  for (const geometry of geometries) geometry.dispose()

  if (!merged) {
    // mergeGeometries returns null only on mismatched attributes — impossible here, since every
    // input comes from the same builder above. Surfaced rather than silently drawing nothing
    // (FR-028, constitution §2.VIII).
    throw new Error(`Could not merge ${geometries.length} building footprints into one geometry.`)
  }

  const mesh = new THREE.Mesh(merged, buildFootprintMaterial(showMass))
  mesh.castShadow = true
  mesh.receiveShadow = true
  mesh.userData = { buildingIds }

  return { mesh, buildingIds, extentMetres, tallestHeightMetres, siteReachMetres, excludedCount }
}

/** T034, FR-016 — flips `colorWrite`/`depthWrite` on the one shared material, mirroring
 * `buildFootprintMaterial`'s own rule, so the developer toggle need not rebuild geometry to take
 * effect. Every building is revealed or hidden together because there is only one material. */
export function setShowMass(mesh: THREE.Mesh, showMass: boolean): void {
  const material = mesh.material as THREE.MeshStandardMaterial
  material.colorWrite = showMass
  material.depthWrite = showMass
}
