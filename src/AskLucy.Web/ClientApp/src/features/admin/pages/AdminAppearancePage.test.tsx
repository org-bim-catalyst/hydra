import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import type { PresenceSphereLook } from '../../chat/scene/sphereConstants'
import { AdminAppearancePage } from './AdminAppearancePage'

// WebGL does not exist in jsdom: the preview is replaced by something that shows the look it was given.
vi.mock('../../chat/components/AiPresenceCard', () => ({
  PresenceCardFrame: ({ look }: { look: PresenceSphereLook }) => (
    <div
      data-testid="card"
      data-dot={look.dotSizeMultiplier}
      data-fill={look.cardFillPercent}
      data-zoom={String(look.zoomEnabled)}
    />
  ),
}))

const saved = {
  dotSizeMultiplier: 1,
  cardFillPercent: 75,
  zoomEnabled: false,
  modifiedBy: 'Ada Lovelace',
  modifiedAtUtc: '2026-10-05T12:00:00Z',
  isDefault: false,
}

let permissions = ['admin.appearance.view', 'admin.appearance.manage']
let putBodies: unknown[] = []

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: ['User'], permissions }),
  ),
  http.get('*/api/v1/appearance/presence-sphere', () => HttpResponse.json(saved)),
  http.put('*/api/v1/appearance/presence-sphere', async ({ request }) => {
    const body = (await request.json()) as PresenceSphereLook
    putBodies.push(body)
    return HttpResponse.json({ ...saved, ...body, modifiedBy: 'Grace Hopper', isDefault: false })
  }),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  permissions = ['admin.appearance.view', 'admin.appearance.manage']
  putBodies = []
})
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(
    [
      { path: '/admin/appearance', element: <AdminAppearancePage /> },
      { path: '/elsewhere', element: <div>Elsewhere</div> },
    ],
    { initialEntries: ['/admin/appearance'] },
  )
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
  return router
}

const card = () => screen.getByTestId('card')
const dotSlider = () => document.querySelector<HTMLInputElement>('input[aria-labelledby="dot-size-label"]') as HTMLInputElement
const fillSlider = () => document.querySelector<HTMLInputElement>('input[aria-labelledby="card-fill-label"]') as HTMLInputElement
const zoomSwitch = () => screen.getByLabelText('Allow users to zoom the sphere') as HTMLInputElement
/** Role queries trip a jsdom bug once a dialog is open, so buttons are found by their text. */
const button = (text: string) => screen.getByText(text).closest('button') as HTMLButtonElement
const loaded = () => screen.findByTestId('card')

describe('AdminAppearancePage', () => {
  it('shows the saved settings, who last changed them, and a preview of them (FR-007, FR-008, FR-013)', async () => {
    renderPage()
    await loaded()

    expect(card()).toHaveAttribute('data-dot', '1')
    expect(card()).toHaveAttribute('data-fill', '75')
    expect(card()).toHaveAttribute('data-zoom', 'false')
    expect(dotSlider().value).toBe('1')
    expect(fillSlider().value).toBe('75')
    expect(zoomSwitch().checked).toBe(false)
    expect(screen.getByText(/Last changed by Ada Lovelace/)).toBeInTheDocument()
  })

  it('updates the preview as a control moves, without saving anything (US1, FR-008, FR-009)', async () => {
    renderPage()
    await loaded()

    fireEvent.change(dotSlider(), { target: { value: '0.5' } })
    fireEvent.change(fillSlider(), { target: { value: '60' } })

    expect(card()).toHaveAttribute('data-dot', '0.5')
    expect(card()).toHaveAttribute('data-fill', '60')
    expect(putBodies).toEqual([])
  })

  it('previews the zoom switch before it is saved (US2)', async () => {
    renderPage()
    await loaded()

    fireEvent.click(zoomSwitch())

    expect(card()).toHaveAttribute('data-zoom', 'true')
    expect(putBodies).toEqual([])
  })

  it('saves all three values, confirms it, and then has nothing left to save (FR-010)', async () => {
    renderPage()
    await loaded()
    expect(button('Save')).toBeDisabled()

    fireEvent.change(dotSlider(), { target: { value: '0.5' } })
    fireEvent.change(fillSlider(), { target: { value: '60' } })
    fireEvent.click(zoomSwitch())
    fireEvent.click(button('Save'))

    await waitFor(() => expect(putBodies).toEqual([{ dotSizeMultiplier: 0.5, cardFillPercent: 60, zoomEnabled: true }]))
    expect(await screen.findByText(/Appearance saved/)).toBeInTheDocument()
    await waitFor(() => expect(button('Save')).toBeDisabled())
    expect(screen.getByText(/Last changed by Grace Hopper/)).toBeInTheDocument()
    expect(card()).toHaveAttribute('data-dot', '0.5')
  })

  it('says the save failed and keeps what is on screen so it can be tried again (FR-015)', async () => {
    server.use(
      http.put('*/api/v1/appearance/presence-sphere', () =>
        HttpResponse.json(
          { title: 'Bad Request', status: 400, detail: 'dotSizeMultiplier must be between 0.25 and 2.00.' },
          { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    renderPage()
    await loaded()

    fireEvent.change(dotSlider(), { target: { value: '0.5' } })
    fireEvent.click(button('Save'))

    expect(await screen.findByText(/Not saved\. dotSizeMultiplier must be between/)).toBeInTheDocument()
    expect(card()).toHaveAttribute('data-dot', '0.5')
    expect(button('Save')).toBeEnabled()
  })

  it('"Reset to defaults" shows the defaults without saving, and "Discard changes" returns to the saved values (US3)', async () => {
    renderPage()
    await loaded()
    fireEvent.change(dotSlider(), { target: { value: '0.5' } })
    fireEvent.change(fillSlider(), { target: { value: '90' } })
    fireEvent.click(zoomSwitch())

    fireEvent.click(button('Reset to defaults'))

    expect(card()).toHaveAttribute('data-dot', '1')
    expect(card()).toHaveAttribute('data-fill', '75')
    expect(card()).toHaveAttribute('data-zoom', 'false')
    expect(putBodies).toEqual([])

    fireEvent.change(fillSlider(), { target: { value: '50' } })
    fireEvent.click(button('Discard changes'))

    expect(card()).toHaveAttribute('data-fill', '75')
    expect(button('Discard changes')).toBeDisabled()
    expect(putBodies).toEqual([])
  })

  it('shows the settings read-only to someone who can view but not manage (FR-017)', async () => {
    permissions = ['admin.appearance.view']
    renderPage()
    await loaded()
    await screen.findByText(/You can see these settings but not change them/)

    expect(dotSlider()).toBeDisabled()
    expect(fillSlider()).toBeDisabled()
    expect(zoomSwitch()).toBeDisabled()
    expect(screen.queryByText('Save')).not.toBeInTheDocument()
    expect(screen.queryByText('Reset to defaults')).not.toBeInTheDocument()
  })

  it('warns before leaving with unsaved changes, and lets the administrator stay or go (FR-016)', async () => {
    const router = renderPage()
    await loaded()
    fireEvent.change(fillSlider(), { target: { value: '60' } })

    await act(async () => {
      void router.navigate('/elsewhere')
    })
    expect(await screen.findByText('Leave without saving?')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/admin/appearance')

    fireEvent.click(button('Stay'))
    await waitFor(() => expect(screen.queryByText('Leave without saving?')).not.toBeInTheDocument())
    expect(router.state.location.pathname).toBe('/admin/appearance')

    await act(async () => {
      void router.navigate('/elsewhere')
    })
    fireEvent.click(await screen.findByText('Leave'))
    await waitFor(() => expect(router.state.location.pathname).toBe('/elsewhere'))
  })

  it('does not warn when nothing has changed', async () => {
    const router = renderPage()
    await loaded()

    await act(async () => {
      await router.navigate('/elsewhere')
    })

    expect(screen.queryByText('Leave without saving?')).not.toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/elsewhere')
  })

  it('says the settings could not be loaded, and offers a retry (constitution VIII)', async () => {
    let fail = true
    server.use(
      http.get('*/api/v1/appearance/presence-sphere', () =>
        fail ? HttpResponse.json({ title: 'Server error', status: 500 }, { status: 500 }) : HttpResponse.json(saved),
      ),
    )
    renderPage()

    expect(await screen.findByText(/could not be loaded/)).toBeInTheDocument()

    fail = false
    fireEvent.click(button('Retry'))
    await loaded()
    expect(card()).toHaveAttribute('data-fill', '75')
  })
})
