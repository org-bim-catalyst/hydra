import { apiFetch } from '../../../api/httpClient'

/** contracts/building-footprints-endpoint.md — mirrors `BuildingHeightProvenance.cs`'s explicit
 * lower-case wire values (FR-011). */
export type BuildingHeightProvenance = 'known' | 'assumed'

export interface SiteBuildingDto {
  id: string
  ring: { latitude: number; longitude: number }[]
  heightMetres: number
  heightProvenance: BuildingHeightProvenance
  name: string
  isSiteBuilding: boolean
}

export interface SiteBuildingsResponse {
  buildings: SiteBuildingDto[]
  limited: boolean
  excludedCount: number
  radiusMetres: number
}

/** GET /api/v1/site-buildings (contracts/building-footprints-endpoint.md). A `503` (building data
 * temporarily unavailable) rejects this promise like any other failed request — callers (T042)
 * catch it and drive the analysis's `partial` state (FR-014), never swallowing it. */
export const getSiteBuildings = (latitude: number, longitude: number, radiusMetres?: number) =>
  apiFetch<SiteBuildingsResponse>(
    `/site-buildings?latitude=${latitude}&longitude=${longitude}${radiusMetres ? `&radiusMetres=${radiusMetres}` : ''}`,
  )
