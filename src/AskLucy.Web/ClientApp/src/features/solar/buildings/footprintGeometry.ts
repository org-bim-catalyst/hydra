import * as THREE from 'three'
import { worldToLocal } from '../../../viewer/api/coordinateFrame'
import type { SiteBuildingDto } from '../api/siteBuildingsApi'

/**
 * research D13 — building meshes cast shadows without being drawn: the basemap already draws its
 * own 3D buildings, and a second copy from a different data source reads as a shifted "ghost
 * duplicate" rather than information. `colorWrite: false` / `depthWrite: false` removes the mesh
 * from the colour and depth passes while `castShadow: true` keeps it in the separate shadow-map
 * depth pass, so it casts a real shadow while drawing nothing. `showMass` is a developer-only
 * toggle that flips `colorWrite` back on to verify the massing (FR-010 as amended).
 */
export function buildFootprintMesh(building: SiteBuildingDto, showMass: boolean): THREE.Mesh | null {
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
  const material = new THREE.MeshStandardMaterial({
    color: building.isSiteBuilding ? 0xffcf8a : 0x9aa4b8,
    roughness: 0.9,
    metalness: 0.05,
    colorWrite: showMass,
    depthWrite: showMass,
  })

  const mesh = new THREE.Mesh(geometry, material)
  mesh.castShadow = true
  mesh.receiveShadow = true
  mesh.userData = { buildingId: building.id, isSiteBuilding: building.isSiteBuilding }
  return mesh
}

/** What the shadow rig needs to know about the footprints, measured while they are built rather
 * than by a second pass over the source data (T033). */
export interface FootprintBuildResult {
  meshes: THREE.Mesh[]
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
 * T033 — builds every footprint and reports the extent and tallest height in the same pass, so the
 * shadow radius (T023) is derived from what was actually built rather than re-deriving it from the
 * DTOs. Returning zero for both on an empty list is deliberate: the caller applies the stated
 * no-buildings fallback (FR-013) rather than this module inventing one.
 */
export function buildFootprintMeshes(buildings: SiteBuildingDto[], showMass: boolean): FootprintBuildResult {
  const meshes: THREE.Mesh[] = []
  let extentMetres = 0
  let tallestHeightMetres = 0
  let excludedCount = 0

  for (const building of buildings) {
    const mesh = buildFootprintMesh(building, showMass)
    if (!mesh) {
      excludedCount += 1
      continue
    }
    meshes.push(mesh)
    tallestHeightMetres = Math.max(tallestHeightMetres, building.heightMetres)
    for (const point of building.ring) {
      const local = worldToLocal(point, 0)
      extentMetres = Math.max(extentMetres, Math.hypot(local.x, local.y))
    }
  }

  return { meshes, extentMetres, tallestHeightMetres, excludedCount }
}

/** T041 — flips `colorWrite`/`depthWrite` on an already-built mesh, mirroring the constructor's
 * own rule, so the developer toggle need not rebuild geometry to take effect. */
export function setShowMass(mesh: THREE.Mesh, showMass: boolean): void {
  const material = mesh.material as THREE.MeshStandardMaterial
  material.colorWrite = showMass
  material.depthWrite = showMass
}
