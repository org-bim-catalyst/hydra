import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../../api/httpClient'
import { useActiveSiteBoundaryStore } from '../../../store/activeSiteBoundaryStore'
import { useOutlineResetStore } from '../../../viewer/siteBoundaryEdit/outlineResetStore'
import { SiteBoundaryResetDialog } from './SiteBoundaryResetDialog'

const chatsApi = vi.hoisted(() => ({ resetSiteBoundary: vi.fn(), getChatById: vi.fn() }))
vi.mock('../../chat/api/chatsApi', () => chatsApi)

const RING = [
  { latitude: 23.586, longitude: 58.392 },
  { latitude: 23.586, longitude: 58.394 },
  { latitude: 23.587, longitude: 58.394 },
]

const found = {
  siteName: 'Muscat Grand Mall',
  centroid: { latitude: 23.5865, longitude: 58.393 },
  polygon: RING,
  additionalPolygons: [],
  areaSquareMeters: 15_000,
  confidence: 0.7,
  confidenceLevel: 'medium',
  source: 'OsmBoundary',
  sourceDetail: 'x',
  revision: 'rev-3',
  isHandEdited: false,
}

function mount() {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <SiteBoundaryResetDialog />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  useActiveSiteBoundaryStore.getState().setBoundary({
    siteName: 'Muscat Grand Mall',
    chatId: 'chat-1',
    centroid: found.centroid,
    polygon: RING,
    areaSquareMeters: 14_000,
    confidence: 0.7,
    confidenceLevel: 'medium',
    source: 'UserCorrected',
    sourceDetail: 'Hand-edited',
    alternativeCandidateNames: [],
    revision: 'rev-2',
    isHandEdited: true,
  })
  useOutlineResetStore.getState().show()
})

afterEach(() => {
  cleanup()
  useOutlineResetStore.getState().hide()
  useActiveSiteBoundaryStore.getState().clearBoundary()
})

describe('SiteBoundaryResetDialog', () => {
  it('asks first, and sends nothing until the user confirms', () => {
    mount()
    expect(screen.getByText(/will be discarded/)).toBeInTheDocument()
    expect(chatsApi.resetSiteBoundary).not.toHaveBeenCalled()
  })

  it('resets with the revision on screen, shows the found outline, and closes', async () => {
    chatsApi.resetSiteBoundary.mockResolvedValue({ activeBoundary: found, message: {} })
    mount()

    fireEvent.click(screen.getByText('Reset'))

    await waitFor(() => expect(useOutlineResetStore.getState().open).toBe(false))
    expect(chatsApi.resetSiteBoundary).toHaveBeenCalledWith('chat-1', { expectedRevision: 'rev-2' })
    expect(useActiveSiteBoundaryStore.getState().isHandEdited).toBe(false)
    expect(useActiveSiteBoundaryStore.getState().revision).toBe('rev-3')
  })

  it('reads the revision from the chat when the outline came from a live reply without one', async () => {
    useActiveSiteBoundaryStore.setState({ revision: null })
    chatsApi.getChatById.mockResolvedValue({ activeBoundary: { revision: 'rev-7' } })
    chatsApi.resetSiteBoundary.mockResolvedValue({ activeBoundary: found, message: {} })
    mount()

    fireEvent.click(screen.getByText('Reset'))

    await waitFor(() => expect(chatsApi.resetSiteBoundary).toHaveBeenCalledWith('chat-1', { expectedRevision: 'rev-7' }))
  })

  it('says the outline changed, and stays open, on a conflict', async () => {
    chatsApi.resetSiteBoundary.mockRejectedValue(new ApiError(409, 'Concurrency conflict', 'changed'))
    mount()

    fireEvent.click(screen.getByText('Reset'))

    expect(await screen.findByText(/changed since you last looked/)).toBeInTheDocument()
    expect(useOutlineResetStore.getState().open).toBe(true)
    expect(useActiveSiteBoundaryStore.getState().isHandEdited).toBe(true)
  })

  it('shows any other failure in the dialog', async () => {
    chatsApi.resetSiteBoundary.mockRejectedValue(new Error('Network down'))
    mount()

    fireEvent.click(screen.getByText('Reset'))

    expect(await screen.findByText('Network down')).toBeInTheDocument()
  })

  it('Keep my edits closes without resetting', () => {
    mount()
    fireEvent.click(screen.getByText('Keep my edits'))
    expect(useOutlineResetStore.getState().open).toBe(false)
    expect(chatsApi.resetSiteBoundary).not.toHaveBeenCalled()
  })
})
