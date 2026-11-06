import { useEffect, useRef } from 'react'
import { useGoogleMapsStore } from '../../../viewer/store/googleMapsStore'
import { useSpatialLayersStore, type SpatialLayer } from '../../../store/spatialLayersStore'

/** Injected once — idempotent via the element-id guard. Every shape reads its colour from the
 * `--spatial-color` custom property so one stylesheet serves every layer. */
function ensureSpatialLayerStyles() {
  if (typeof document === 'undefined' || document.getElementById('spatial-layer-styles')) return

  const style = document.createElement('style')
  style.id = 'spatial-layer-styles'
  style.textContent = `
    .spatial-layer-marker { position: relative; width: 0; height: 0; }

    .spatial-layer-marker__shape { position: absolute; }

    .spatial-layer-marker--classic-pin .spatial-layer-marker__shape {
      width: 18px; height: 18px; border-radius: 50% 50% 50% 0;
      background: var(--spatial-color);
      top: -22px; left: -9px;
      transform: rotate(-45deg);
      box-shadow: 0 1px 3px rgba(0,0,0,0.45);
    }

    .spatial-layer-marker--pulsing-ring .spatial-layer-marker__ring {
      width: 22px; height: 22px; border-radius: 50%;
      border: 3px solid var(--spatial-color);
      top: -11px; left: -11px;
      animation: spatial-layer-pulse 1.8s ease-out infinite;
    }
    @keyframes spatial-layer-pulse {
      0%   { transform: scale(1);   opacity: 1; }
      70%  { transform: scale(2);   opacity: 0; }
      100% { transform: scale(2);   opacity: 0; }
    }

    .spatial-layer-marker--3d-highlight .spatial-layer-marker__shape {
      width: 24px; height: 24px; border-radius: 4px;
      background: var(--spatial-color);
      opacity: 0.35;
      border: 2px solid var(--spatial-color);
      box-shadow: 0 0 10px 3px var(--spatial-color);
      top: -12px; left: -12px;
    }

    .spatial-layer-marker--simple-dot .spatial-layer-marker__shape {
      width: 12px; height: 12px; border-radius: 50%;
      background: var(--spatial-color);
      border: 1.5px solid rgba(255,255,255,0.85);
      box-shadow: 0 1px 3px rgba(0,0,0,0.5);
      top: -6px; left: -6px;
    }

    .spatial-layer-marker__label {
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
  root.className = `spatial-layer-marker spatial-layer-marker--${shape}`
  root.style.setProperty('--spatial-color', color)
  root.setAttribute('role', 'img')
  root.setAttribute('aria-label', name || 'Map feature')

  const shapeEl = document.createElement('div')
  shapeEl.className = shape === 'pulsing-ring' ? 'spatial-layer-marker__ring' : 'spatial-layer-marker__shape'
  shapeEl.setAttribute('aria-hidden', 'true')
  root.appendChild(shapeEl)

  if (name) {
    const label = document.createElement('div')
    label.className = 'spatial-layer-marker__label'
    label.textContent = name
    label.setAttribute('aria-hidden', 'true')
    root.appendChild(label)
  }

  return root
}

const DEFAULT_MARKER_COLOR = '#4285F4'
const DEFAULT_AREA_COLOR = '#1976D2'

/**
 * specs/094-spatial-analysis-orchestrator — draws every visible spatial layer on the Google map:
 * marker layers as advanced markers, buffer/area/band layers as polygons. Follows
 * `POIMarkerOverlay`'s idiom exactly: no DOM output, purely imperative, and it replaces the whole
 * rendered set whenever the layer store changes (add, keep, toggle, remove or clear).
 */
export function SpatialLayersOverlay() {
  const map = useGoogleMapsStore((s) => s.map)
  const layers = useSpatialLayersStore((s) => s.layers)
  const markersRef = useRef<google.maps.marker.AdvancedMarkerElement[]>([])
  const polygonsRef = useRef<google.maps.Polygon[]>([])

  useEffect(() => {
    if (!map) {
      return
    }

    const visible = layers.filter((layer) => layer.visible)
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

        for (const polygon of polygonsRef.current) {
          polygon.setMap(null)
        }
        polygonsRef.current = []

        for (const layer of visible) {
          if (layer.kind === 'markers') {
            const color = layer.style?.color ?? DEFAULT_MARKER_COLOR
            const shape = layer.style?.markerShape ?? 'simple-dot'
            for (const feature of layer.markers ?? []) {
              const content = buildMarkerContent(feature.name, shape, color)
              markersRef.current.push(
                new AdvancedMarkerElement({
                  map,
                  position: { lat: feature.latitude, lng: feature.longitude },
                  content,
                  title: feature.name || layer.legendLabel,
                }),
              )
            }
            continue
          }

          if ((layer.ring?.length ?? 0) >= 3) {
            const color = layer.style?.color ?? DEFAULT_AREA_COLOR
            polygonsRef.current.push(
              new google.maps.Polygon({
                map,
                paths: layer.ring!.map((point) => ({ lat: point.latitude, lng: point.longitude })),
                strokeColor: color,
                strokeOpacity: 0.85,
                strokeWeight: layer.style?.strokeWidth ?? 2,
                fillColor: color,
                fillOpacity: layer.style?.opacity ?? 0.25,
              }),
            )
          }
        }

        // Frame what was just drawn. Without this, a search run from anywhere else puts every
        // feature off-screen and the reply says "shown" while the user sees nothing.
        const points = visible.flatMap((layer) => [
          ...(layer.markers ?? []).map((marker) => ({ lat: marker.latitude, lng: marker.longitude })),
          ...(layer.ring ?? []).map((point) => ({ lat: point.latitude, lng: point.longitude })),
        ])

        if (points.length === 1) {
          map.setCenter(points[0])
          map.setZoom(14)
        } else if (points.length > 1) {
          const bounds = new google.maps.LatLngBounds()
          for (const point of points) {
            bounds.extend(point)
          }
          map.fitBounds(bounds, 48)
        }
      } catch (err) {
        console.error('[SpatialLayersOverlay] Failed to draw spatial layers.', err)
      }
    })()

    return () => {
      cancelled = true
    }
  }, [map, layers])

  useEffect(() => {
    ensureSpatialLayerStyles()
    return () => {
      for (const marker of markersRef.current) {
        marker.map = null
      }
      markersRef.current = []
      for (const polygon of polygonsRef.current) {
        polygon.setMap(null)
      }
      polygonsRef.current = []
    }
  }, [])

  return null
}

/** Exported for the legend's swatch so the two never disagree about a layer's look. */
export function styleOf(layer: SpatialLayer) {
  return {
    color: layer.style?.color ?? (layer.kind === 'markers' ? DEFAULT_MARKER_COLOR : DEFAULT_AREA_COLOR),
    markerShape: layer.style?.markerShape ?? 'simple-dot',
    opacity: layer.style?.opacity ?? 0.25,
  }
}
