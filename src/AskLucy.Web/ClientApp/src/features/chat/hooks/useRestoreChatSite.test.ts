import { act, cleanup, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { useActiveLocationStore } from '../../../store/activeLocationStore'
import { useActiveSiteBoundaryStore } from '../../../store/activeSiteBoundaryStore'
import type { ChatActiveBoundary, ChatDetail } from '../api/chatsApi'
import { useRestoreChatSite } from './useRestoreChatSite'

const boundary: ChatActiveBoundary = {
  siteName: 'Al Safa Park 2',
  centroid: { latitude: 25.156, longitude: 55.222 },
  polygon: [
    { latitude: 25.15, longitude: 55.22 },
    { latitude: 25.16, longitude: 55.22 },
    { latitude: 25.16, longitude: 55.23 },
    { latitude: 25.15, longitude: 55.22 },
  ],
  areaSquareMeters: 15146.14,
  confidence: 0.92,
  confidenceLevel: 'high',
  source: 'OsmBoundary',
  sourceDetail: 'OpenStreetMap (leisure=park)',
}

function chatWithSite(overrides: Partial<ChatDetail> = {}): ChatDetail {
  return {
    id: 'chat-1',
    title: 'Park survey',
    providerId: null,
    modelId: null,
    activeLocation: { latitude: 25.1558, longitude: 55.2218, locationName: 'Al Safa Park 2', confidence: 0.9, confidenceLevel: 'high' },
    activeBoundary: boundary,
    ...overrides,
  }
}

afterEach(() => {
  // Unmount first: a still-mounted hook would (correctly) restore the site the moment the store
  // is cleared below.
  cleanup()
  useActiveLocationStore.getState().clear()
  useActiveSiteBoundaryStore.getState().clearBoundary()
})

describe('useRestoreChatSite', () => {
  it("puts the chat's confirmed site and its outline back on the viewer", () => {
    act(() => useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278))

    renderHook(() => useRestoreChatSite(chatWithSite()))

    expect(useActiveLocationStore.getState()).toMatchObject({
      source: 'agent',
      latitude: 25.1558,
      longitude: 55.2218,
      locationName: 'Al Safa Park 2',
      confidenceLevel: 'high',
    })
    expect(useActiveSiteBoundaryStore.getState()).toMatchObject({
      siteName: 'Al Safa Park 2',
      confidenceLevel: 'high',
      source: 'OsmBoundary',
      polygon: boundary.polygon,
      alternativeCandidateNames: [],
    })
  })

  it('does nothing while the chat detail is still loading, or when the chat never confirmed a site', () => {
    act(() => useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278))

    const { rerender } = renderHook(({ detail }) => useRestoreChatSite(detail), {
      initialProps: { detail: undefined as ChatDetail | undefined },
    })
    rerender({ detail: chatWithSite({ activeLocation: null, activeBoundary: null }) })

    expect(useActiveLocationStore.getState()).toMatchObject({ source: 'geolocation', latitude: 51.5074 })
    expect(useActiveSiteBoundaryStore.getState().siteName).toBeNull()
  })

  it('leaves a site Lucy already put on screen this session where it is', () => {
    act(() => useActiveLocationStore.getState().setFromAgent(40.7829, -73.9654, 'Central Park', 0.95))

    renderHook(() => useRestoreChatSite(chatWithSite()))

    expect(useActiveLocationStore.getState()).toMatchObject({ source: 'agent', locationName: 'Central Park' })
    expect(useActiveSiteBoundaryStore.getState().siteName).toBeNull()
  })

  it('restores again once the device location is cleared from under it', () => {
    renderHook(() => useRestoreChatSite(chatWithSite()))
    expect(useActiveLocationStore.getState().locationName).toBe('Al Safa Park 2')

    // ChatPage clears the store when geolocation turns out to be unavailable — which can land
    // after the chat detail did.
    act(() => useActiveLocationStore.getState().clear())

    expect(useActiveLocationStore.getState()).toMatchObject({ source: 'agent', locationName: 'Al Safa Park 2' })
  })

  it("clears another site's outline when the chat has a location but no boundary", () => {
    act(() => useActiveSiteBoundaryStore.getState().setBoundary({ ...boundary, siteName: 'Zabeel Park', source: 'OsmBoundary', alternativeCandidateNames: [] }))

    renderHook(() => useRestoreChatSite(chatWithSite({ activeBoundary: null })))

    expect(useActiveLocationStore.getState().locationName).toBe('Al Safa Park 2')
    expect(useActiveSiteBoundaryStore.getState().siteName).toBeNull()
  })
})
