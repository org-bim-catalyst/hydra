import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, describe, expect, it } from 'vitest'
import { useAuthStore } from '../store/authStore'
import { PublicOnlyRoute } from './PublicOnlyRoute'

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route
          path="/"
          element={
            <PublicOnlyRoute>
              <div>Landing content</div>
            </PublicOnlyRoute>
          }
        />
        <Route path="/studio" element={<div>Workspace</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('PublicOnlyRoute (spec.md FR-015, contracts/routing-and-consent-contract.md)', () => {
  afterEach(() => {
    useAuthStore.setState({ accessToken: null, refreshToken: null, userId: null })
  })

  it('renders its children for a signed-out visitor', () => {
    renderAt('/')

    expect(screen.getByText('Landing content')).toBeInTheDocument()
  })

  it('redirects an already-authenticated visitor straight into the workspace', () => {
    useAuthStore.setState({ accessToken: 'token-123', refreshToken: 'refresh-123', userId: 'user-1' })

    renderAt('/')

    expect(screen.getByText('Workspace')).toBeInTheDocument()
    expect(screen.queryByText('Landing content')).not.toBeInTheDocument()
  })
})
