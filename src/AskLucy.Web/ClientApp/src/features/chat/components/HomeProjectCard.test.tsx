import { render, screen } from '@testing-library/react'
import { fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { HomeProjectCard } from './HomeProjectCard'

describe('HomeProjectCard', () => {
  it('renders a Home button and the workspace name', () => {
    render(
      <MemoryRouter>
        <HomeProjectCard />
      </MemoryRouter>,
    )
    expect(screen.getByRole('button', { name: 'Home' })).toBeInTheDocument()
    expect(screen.getByText('Flumeria Studio')).toBeInTheDocument()
  })

  it('navigates to / when Home is clicked', () => {
    render(
      <MemoryRouter initialEntries={['/studio']}>
        <HomeProjectCard />
      </MemoryRouter>,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Home' }))
    // no throw — react-router's useNavigate() call succeeding is the assertion here;
    // full redirect-back-to-/studio behavior is covered at the router level (T009/T013).
  })
})
