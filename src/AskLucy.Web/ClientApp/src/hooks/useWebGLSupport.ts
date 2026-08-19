import { useState } from 'react'

/** Independent WebGL2 capability probe for the viewer engine (specs/027-immersive-viewer-platform).
 * Deliberately does NOT share code with `features/chat/scene/useSceneQualityTier.ts` — that hook
 * drives the existing, protected `AiPresenceCard` scene, which FR-004 requires stay unaffected by
 * this feature. Duplicating this few-line check is acceptable per constitution §2.III (DRY governs
 * business logic, not a capability probe). */
export function supportsWebGL2(): boolean {
  if (typeof document === 'undefined') return false
  try {
    const canvas = document.createElement('canvas')
    return Boolean(canvas.getContext('webgl2'))
  } catch {
    return false
  }
}

/** Whether the current browser can render the viewer's interactive content (FR-005). Computed
 * once per mount — WebGL2 support doesn't change over a session. */
export function useWebGLSupport(): boolean {
  const [supported] = useState(supportsWebGL2)
  return supported
}
