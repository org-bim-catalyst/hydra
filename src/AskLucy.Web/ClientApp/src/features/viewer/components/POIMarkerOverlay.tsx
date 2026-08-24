import { useEffect, useRef } from 'react'
import { useActiveLocationStore } from '../../../store/activeLocationStore'
import { useGoogleMapsStore } from '../../../viewer/store/googleMapsStore'
import { useMarkerStyleStore, type MarkerStyle } from '../../../store/markerStyleStore'

/** Injects the global CSS for POI marker styles once — idempotent via the element id guard. */
function ensurePoiStyles() {
  if (typeof document === 'undefined' || document.getElementById('poi-marker-styles')) return
  const style = document.createElement('style')
  style.id = 'poi-marker-styles'
  style.textContent = `
    .poi-marker { position: relative; display: flex; flex-direction: column; align-items: center; }

    /* pulsing-ring */
    .poi-marker--pulsing-ring .poi-marker__ring {
      width: 24px; height: 24px; border-radius: 50%;
      border: 3px solid #4285F4;
      animation: poi-pulse 1.6s ease-out infinite;
    }
    @keyframes poi-pulse {
      0%   { transform: scale(1);   opacity: 1; }
      70%  { transform: scale(2.2); opacity: 0; }
      100% { transform: scale(2.2); opacity: 0; }
    }

    /* classic-pin */
    .poi-marker--classic-pin .poi-marker__ring {
      width: 20px; height: 20px; border-radius: 50% 50% 50% 0;
      background: #EA4335; transform: rotate(-45deg);
    }

    /* 3d-highlight */
    .poi-marker--3d-highlight .poi-marker__ring {
      width: 28px; height: 28px; border-radius: 4px;
      background: rgba(66, 133, 244, 0.25);
      border: 2px solid #4285F4;
      box-shadow: 0 0 12px 4px rgba(66,133,244,0.45);
    }

    /* simple-dot */
    .poi-marker--simple-dot .poi-marker__ring {
      width: 12px; height: 12px; border-radius: 50%;
      background: #4285F4;
    }

    .poi-marker__label {
      margin-top: 4px;
      background: rgba(0,0,0,0.65);
      color: #fff;
      font-size: 11px;
      padding: 2px 6px;
      border-radius: 3px;
      white-space: nowrap;
      max-width: 180px;
      overflow: hidden;
      text-overflow: ellipsis;
      pointer-events: none;
    }
  `
  document.head.appendChild(style)
}

function buildMarkerContent(locationName: string, style: MarkerStyle): HTMLElement {
  const root = document.createElement('div')
  root.className = `poi-marker poi-marker--${style}`
  root.setAttribute('role', 'img')
  root.setAttribute('aria-label', locationName || 'Point of interest')

  const ring = document.createElement('div')
  ring.className = 'poi-marker__ring'
  ring.setAttribute('aria-hidden', 'true')

  const label = document.createElement('div')
  label.className = 'poi-marker__label'
  label.textContent = locationName
  label.setAttribute('aria-hidden', 'true')

  root.appendChild(ring)
  if (locationName) root.appendChild(label)
  return root
}

/** specs/038-viewer-poi-zoom: renders a POI marker on the Google Maps instance for the
 * agent-confirmed active location. Replaces the previous marker on every location change
 * (T039 — `marker.map = null` before creating a new one). Returns null — manages the marker
 * imperatively via the Google Maps API. */
export function POIMarkerOverlay() {
  const map = useGoogleMapsStore((s) => s.map)
  const latitude = useActiveLocationStore((s) => s.latitude)
  const longitude = useActiveLocationStore((s) => s.longitude)
  const locationName = useActiveLocationStore((s) => s.locationName)
  const source = useActiveLocationStore((s) => s.source)
  const markerStyle = useMarkerStyleStore((s) => s.markerStyle)

  // Holds the live AdvancedMarkerElement so cleanup can remove it.
  const markerRef = useRef<google.maps.marker.AdvancedMarkerElement | null>(null)

  useEffect(() => {
    ensurePoiStyles()

    // No marker when: map not ready, no location, or location from device (not agent).
    if (!map || latitude === null || longitude === null || source !== 'agent') {
      if (markerRef.current) {
        markerRef.current.map = null
        markerRef.current = null
      }
      return
    }

    let cancelled = false

    void (async () => {
      try {
        const { AdvancedMarkerElement } = (await google.maps.importLibrary(
          'marker',
        )) as google.maps.MarkerLibrary
        if (cancelled) return

        // T039: remove the previous marker before placing a new one — only one marker at a time.
        if (markerRef.current) {
          markerRef.current.map = null
        }

        const content = buildMarkerContent(locationName ?? '', markerStyle)

        markerRef.current = new AdvancedMarkerElement({
          map,
          position: { lat: latitude, lng: longitude },
          content,
          title: locationName ?? 'Point of interest',
        })
      } catch (err) {
        console.error('[POIMarkerOverlay] Failed to create POI marker.', err)
      }
    })()

    return () => {
      cancelled = true
    }
  }, [map, latitude, longitude, source, locationName, markerStyle])

  // Cleanup on unmount.
  useEffect(() => {
    return () => {
      if (markerRef.current) {
        markerRef.current.map = null
        markerRef.current = null
      }
    }
  }, [])

  return null
}
