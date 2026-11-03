import { useEffect, useRef } from 'react'
import { useGoogleMapsStore } from '../../../viewer/store/googleMapsStore'
import { usePoiLayersStore } from '../../../store/poiLayersStore'

/** Injected once — idempotent via the element-id guard. Every shape reads its colour from the
 * `--poi-color` custom property so one stylesheet serves every category. */
function ensurePoiLayerStyles() {
  if (typeof document === 'undefined' || document.getElementById('poi-layer-styles')) return

  const style = document.createElement('style')
  style.id = 'poi-layer-styles'
  style.textContent = `
    .poi-layer-marker { position: relative; width: 0; height: 0; }

    .poi-layer-marker__shape { position: absolute; }

    .poi-layer-marker--classic-pin .poi-layer-marker__shape {
      width: 18px; height: 18px; border-radius: 50% 50% 50% 0;
      background: var(--poi-color);
      top: -22px; left: -9px;
      transform: rotate(-45deg);
      box-shadow: 0 1px 3px rgba(0,0,0,0.45);
    }

    .poi-layer-marker--pulsing-ring .poi-layer-marker__ring {
      width: 22px; height: 22px; border-radius: 50%;
      border: 3px solid var(--poi-color);
      top: -11px; left: -11px;
      animation: poi-layer-pulse 1.8s ease-out infinite;
    }
    @keyframes poi-layer-pulse {
      0%   { transform: scale(1);   opacity: 1; }
      70%  { transform: scale(2);   opacity: 0; }
      100% { transform: scale(2);   opacity: 0; }
    }

    .poi-layer-marker--3d-highlight .poi-layer-marker__shape {
      width: 24px; height: 24px; border-radius: 4px;
      background: var(--poi-color);
      opacity: 0.35;
      border: 2px solid var(--poi-color);
      box-shadow: 0 0 10px 3px var(--poi-color);
      top: -12px; left: -12px;
    }

    .poi-layer-marker--simple-dot .poi-layer-marker__shape {
      width: 12px; height: 12px; border-radius: 50%;
      background: var(--poi-color);
      border: 1.5px solid rgba(255,255,255,0.85);
      box-shadow: 0 1px 3px rgba(0,0,0,0.5);
      top: -6px; left: -6px;
    }

    .poi-layer-marker__label {
      position: absolute;
      top: 14px; left: 0;
      transform: translateX(-50%);
      max-width: 160px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      font-size: 10px;
      padding: 1px 5px;
      border-radius: 3px;
      color: #fff;
      background: rgba(0,0,0,0.62);
      pointer-events: none;
    }
  `
  document.head.appendChild(style)
}

function buildMarkerContent(name: string, shape: string, color: string): HTMLElement {
  const root = document.createElement('div')
  root.className = `poi-layer-marker poi-layer-marker--${shape}`
  root.style.setProperty('--poi-color', color)
  root.setAttribute('role', 'img')
  root.setAttribute('aria-label', name || 'Point of interest')

  const shapeEl = document.createElement('div')
  shapeEl.className = shape === 'pulsing-ring' ? 'poi-layer-marker__ring' : 'poi-layer-marker__shape'
  shapeEl.setAttribute('aria-hidden', 'true')
  root.appendChild(shapeEl)

  if (name) {
    const label = document.createElement('div')
    label.className = 'poi-layer-marker__label'
    label.textContent = name
    label.setAttribute('aria-hidden', 'true')
    root.appendChild(label)
  }

  return root
}

/**
 * specs/090-poi-discovery — draws every active POI layer's markers on the Google map. Follows
 * `POIMarkerOverlay`'s idiom exactly: no DOM output, purely imperative, and it replaces the whole
 * marker set whenever the layer store changes (add, keep, remove or clear).
 */
export function PoiLayersOverlay() {
  const map = useGoogleMapsStore((s) => s.map)
  const layers = usePoiLayersStore((s) => s.layers)
  const markersRef = useRef<google.maps.marker.AdvancedMarkerElement[]>([])

  useEffect(() => {
    if (!map) {
      return
    }

    let cancelled = false

    void (async () => {
      try {
        const { AdvancedMarkerElement } = (await google.maps.importLibrary(
          'marker',
        )) as google.maps.MarkerLibrary
        if (cancelled) return

        for (const marker of markersRef.current) {
          marker.map = null
        }
        markersRef.current = []

        for (const layer of layers) {
          for (const poi of layer.markers) {
            const content = buildMarkerContent(poi.name, layer.markerShape, layer.color)
            markersRef.current.push(
              new AdvancedMarkerElement({
                map,
                position: { lat: poi.latitude, lng: poi.longitude },
                content,
                title: poi.name || layer.displayName,
              }),
            )
          }
        }

        // Frame what was just drawn. Without this, a search run from anywhere else puts every
        // marker off-screen and the reply says "shown" while the user sees nothing.
        const all = layers.flatMap((layer) => layer.markers)
        if (all.length === 1) {
          map.setCenter({ lat: all[0].latitude, lng: all[0].longitude })
          map.setZoom(14)
        } else if (all.length > 1) {
          const bounds = new google.maps.LatLngBounds()
          for (const poi of all) {
            bounds.extend({ lat: poi.latitude, lng: poi.longitude })
          }
          map.fitBounds(bounds, 48)
        }
      } catch (err) {
        console.error('[PoiLayersOverlay] Failed to draw POI layers.', err)
      }
    })()

    return () => {
      cancelled = true
    }
  }, [map, layers])

  useEffect(() => {
    ensurePoiLayerStyles()
    return () => {
      for (const marker of markersRef.current) {
        marker.map = null
      }
      markersRef.current = []
    }
  }, [])

  return null
}
