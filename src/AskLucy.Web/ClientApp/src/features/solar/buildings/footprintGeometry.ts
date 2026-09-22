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
  /** Footprints whose ring was too degenerate to extrude. Counted rather than dropped silently. */
  excludedCount: number
}

/**
 * T031, T032, T033 — builds every footprint, merges them into a single mesh, and reports the
 * extent, the tallest height and the exclusions in the same pass, so neither the shadow radius
 * (T023) nor the exclusion notice (FR-028) has to re-traverse the source data.
 *
 * Returning zero extent/height on an empty list is deliberate: the caller applies the stated
 * no-buildings fallback (FR-013) rather than this module inventing one.
 */
export function buildFootprintMeshes(buildings: SiteBuildingDto[], showMass: boolean): FootprintBuildResult {
  const geometries: THREE.ExtrudeGeometry[] = []
  const buildingIds: string[] = []
  let extentMetres = 0
  let tallestHeightMetres = 0
  let excludedCount = 0

  for (const building of buildings) {
    const geometry = buildFootprintGeometry(building)
    if (!geometry) {
      excludedCount += 1
      continue
    }
    geometries.push(geometry)
    buildingIds.push(building.id)
    tallestHeightMetres = Math.max(tallestHeightMetres, building.heightMetres)
    for (const point of building.ring) {
      const local = worldToLocal(point, 0)
      extentMetres = Math.max(extentMetres, Math.hypot(local.x, local.y))
    }
  }

  if (geometries.length === 0) {
    return { mesh: null, buildingIds, extentMetres, tallestHeightMetres, excludedCount }
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

  return { mesh, buildingIds, extentMetres, tallestHeightMetres, excludedCount }
}

/** T034, FR-016 — flips `colorWrite`/`depthWrite` on the one shared material, mirroring
 * `buildFootprintMaterial`'s own rule, so the developer toggle need not rebuild geometry to take
 * effect. Every building is revealed or hidden together because there is only one material. */
export function setShowMass(mesh: THREE.Mesh, showMass: boolean): void {
  const material = mesh.material as THREE.MeshStandardMaterial
  material.colorWrite = showMass
  material.depthWrite = showMass
}
