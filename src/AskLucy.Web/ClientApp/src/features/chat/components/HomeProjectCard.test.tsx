import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { describe, expect, it } from 'vitest'
import { HomeProjectCard } from './HomeProjectCard'

function LandingProbe() {
  const location = useLocation()
  return <div>Landing, state: {JSON.stringify(location.state)}</div>
}

function renderCard() {
  return render(
    <MemoryRouter initialEntries={['/studio']}>
      <Routes>
        <Route path="/studio" element={<HomeProjectCard />} />
        <Route path="/" element={<LandingProbe />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('HomeProjectCard', () => {
  it('shows the workspace name as a plain title, not a link or button', () => {
    renderCard()

    expect(screen.getByText('Flumeria Studio')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Flumeria Studio' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Flumeria Studio' })).not.toBeInTheDocument()
  })

  // specs/073 — the pair are the first two items of the studio's HUD row, which positions them.
  it('renders the Home button and the title as sibling row items, without positioning itself', () => {
    const { container } = render(
      <MemoryRouter>
        <HomeProjectCard />
      </MemoryRouter>,
    )

    const home = screen.getByRole('button', { name: 'Home' })
    const titleCard = screen.getByText('Flumeria Studio').parentElement as HTMLElement
    expect(Array.from(container.children)).toEqual([home, titleCard])
    for (const item of [home, titleCard]) {
      expect(window.getComputedStyle(item).position).not.toBe('absolute')
    }
    expect(window.getComputedStyle(titleCard).height).toBe('40px')
  })

  it('opens the landing page from Home, asking PublicOnlyRoute not to bounce a signed-in user back', () => {
    renderCard()

    fireEvent.click(screen.getByRole('button', { name: 'Home' }))

    expect(screen.getByText('Landing, state: {"viewLanding":true}')).toBeInTheDocument()
  })
})
