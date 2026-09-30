import { useEffect, useRef } from 'react'
import { useGoogleMapsStore } from '../store/googleMapsStore'
import { createGooglePixelProjector, type PixelProjector } from './googlePixelProjector'

/**
 * specs/079: the map's pixel projector, held for as long as a tool that needs it is on screen. The
 * projector is an external resource (an invisible OverlayView on the map), so a ref the effect owns
 * keeps it: made when `active` turns on, released when it turns off. `injected` is the test seam - a
 * projector passed in (or `null`, "the map is not ready") is used as given and nothing is created.
 *
 * Returns a getter rather than the projector, so a pointer handler always reads the current one.
 */
export function usePixelProjector(active: boolean, injected?: PixelProjector | null): () => PixelProjector | null {
  const map = useGoogleMapsStore((s) => s.map)
  const ref = useRef<PixelProjector | null>(null)

  useEffect(() => {
    if (injected !== undefined || !active || !map) return

    const created = createGooglePixelProjector(map)
    ref.current = created
    return () => {
      created.dispose()
      ref.current = null
    }
  }, [active, map, injected])

  return () => (injected !== undefined ? injected : ref.current)
}
