import { apiFetch } from '../../../api/httpClient'
import type { PresenceSphereLook } from '../scene/sphereConstants'

/** specs/080 contracts/presence-sphere-api.md. */
export interface PresenceSphereSettings extends PresenceSphereLook {
  /** A display name, never an e-mail address; null until the first save or if that person can no longer be found. */
  modifiedBy: string | null
  modifiedAtUtc: string | null
  /** True while nobody has saved any settings: the defaults apply. */
  isDefault: boolean
}

const PATH = '/appearance/presence-sphere'

export const getPresenceSphereSettings = () => apiFetch<PresenceSphereSettings>(PATH)

export const updatePresenceSphereSettings = (look: PresenceSphereLook) =>
  apiFetch<PresenceSphereSettings>(PATH, { method: 'PUT', body: JSON.stringify(look) })
