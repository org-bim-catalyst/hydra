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

/** T041 — flips `colorWrite`/`depthWrite` on an already-built mesh, mirroring the constructor's
 * own rule, so the developer toggle need not rebuild geometry to take effect. */
export function setShowMass(mesh: THREE.Mesh, showMass: boolean): void {
  const material = mesh.material as THREE.MeshStandardMaterial
  material.colorWrite = showMass
  material.depthWrite = showMass
}
