import { create } from 'zustand'

export type MarkerStyle = 'pulsing-ring' | 'classic-pin' | '3d-highlight' | 'simple-dot'

const STORAGE_KEY = 'viewer.markerStyle'
const DEFAULT_STYLE: MarkerStyle = 'pulsing-ring'
const VALID_STYLES: readonly MarkerStyle[] = ['pulsing-ring', 'classic-pin', '3d-highlight', 'simple-dot']

function getDefaultMarkerStyle(): MarkerStyle {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored && (VALID_STYLES as readonly string[]).includes(stored)) {
      return stored as MarkerStyle
    }
  } catch {
    // localStorage unavailable (private window, storage quota, sandboxed iframe) — use default.
  }
  return DEFAULT_STYLE
}

interface MarkerStyleState {
  markerStyle: MarkerStyle
  setMarkerStyle: (style: MarkerStyle) => void
}

export const useMarkerStyleStore = create<MarkerStyleState>()((set) => ({
  markerStyle: getDefaultMarkerStyle(),

  setMarkerStyle(style) {
    set({ markerStyle: style })
    try {
      localStorage.setItem(STORAGE_KEY, style)
    } catch {
      // localStorage write failed — style change still applies for this session.
    }
  },
}))
