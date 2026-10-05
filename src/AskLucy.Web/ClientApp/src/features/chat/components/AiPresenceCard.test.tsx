import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as presenceSphereApi from '../api/presenceSphereApi'
import { DEFAULT_SPHERE_LOOK, type PresenceSphereLook } from '../scene/sphereConstants'
import { AiPresenceCard } from './AiPresenceCard'

// WebGL does not exist in jsdom: what matters here is the look the card hands to the scene.
const sceneLooks: PresenceSphereLook[] = []
vi.mock('../scene/SceneBackground', () => ({
  SceneBackground: ({ look }: { look: PresenceSphereLook }) => {
    sceneLooks.push(look)
    return <div data-testid="scene" />
  },
}))

vi.mock('../api/presenceSphereApi')

const bands = () => ({ low: 0, mid: 0, high: 0 })

function renderCard(ui: ReactNode = <AiPresenceCard getFrequencyBands={bands} />) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>)
}

describe('AiPresenceCard', () => {
  beforeEach(() => {
    sceneLooks.length = 0
    vi.mocked(presenceSphereApi.getPresenceSphereSettings).mockResolvedValue({
      ...DEFAULT_SPHERE_LOOK,
      modifiedBy: null,
      modifiedAtUtc: null,
      isDefault: true,
    })
  })

  it('renders as a rounded card matching the readdy.ai reference (FR-023)', () => {
    // oklch() isn't reliably computable in jsdom, so this checks border-radius (the
    // reference's own literal `rounded-lg` = 8px) rather than the sampled oklch color.
    const { container } = renderCard()
    const root = container.firstElementChild as HTMLElement
    expect(root).toHaveStyle({ borderRadius: '8px' })
  })

  it('eventually mounts scene content inside the card (lazy-loaded, research.md #7)', async () => {
    const { container } = renderCard()
    await waitFor(() => {
      expect(container.firstElementChild?.childElementCount).toBeGreaterThan(0)
    })
  })

  it('draws the sphere with the default look straight away, without waiting for the settings (SC-007)', async () => {
    vi.mocked(presenceSphereApi.getPresenceSphereSettings).mockReturnValue(new Promise(() => {}))

    renderCard()

    await waitFor(() => expect(sceneLooks.length).toBeGreaterThan(0))
    expect(sceneLooks[0]).toEqual(DEFAULT_SPHERE_LOOK)
  })

  it("hands the administrator's saved look to the scene (FR-010)", async () => {
    vi.mocked(presenceSphereApi.getPresenceSphereSettings).mockResolvedValue({
      dotSizeMultiplier: 0.5,
      cardFillPercent: 60,
      zoomEnabled: true,
      modifiedBy: 'Ada Lovelace',
      modifiedAtUtc: '2026-10-05T12:00:00Z',
      isDefault: false,
    })

    renderCard()

    await waitFor(() => expect(sceneLooks.at(-1)).toEqual({ dotSizeMultiplier: 0.5, cardFillPercent: 60, zoomEnabled: true }))
  })

  it('keeps the default look and logs the failure when the settings cannot be loaded (FR-014)', async () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {})
    vi.mocked(presenceSphereApi.getPresenceSphereSettings).mockRejectedValue(new Error('network down'))

    renderCard()

    // The hook retries once, a second later, before it gives up.
    await waitFor(() => expect(error).toHaveBeenCalledWith(expect.stringContaining('Presence sphere settings'), expect.any(Error)), {
      timeout: 4000,
    })
    expect(sceneLooks.at(-1)).toEqual(DEFAULT_SPHERE_LOOK)
    error.mockRestore()
  })
})
