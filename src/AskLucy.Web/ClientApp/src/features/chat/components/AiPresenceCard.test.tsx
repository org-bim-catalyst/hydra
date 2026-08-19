import { render, waitFor } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { AiPresenceCard } from './AiPresenceCard'

describe('AiPresenceCard', () => {
  it('renders as a rounded card matching the readdy.ai reference (FR-023)', () => {
    // oklch() isn't reliably computable in jsdom, so this checks border-radius (the
    // reference's own literal `rounded-lg` = 8px) rather than the sampled oklch color.
    const { container } = render(<AiPresenceCard getReactiveIntensity={() => 0} />)
    const root = container.firstElementChild as HTMLElement
    expect(root).toHaveStyle({ borderRadius: '8px' })
  })

  it('eventually mounts scene content inside the card (lazy-loaded, research.md #7)', async () => {
    const { container } = render(<AiPresenceCard getReactiveIntensity={() => 0} />)
    await waitFor(() => {
      expect(container.firstElementChild?.childElementCount).toBeGreaterThan(0)
    })
  })
})
