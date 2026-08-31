import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { AdminShell } from './AdminShell'
import { ADMIN_NAV } from '../adminNav'

function renderShell(pathname = '/admin/dashboard') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[pathname]}>
        <AdminShell title="Admin Dashboard">
          <div>section content</div>
        </AdminShell>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  try {
    localStorage.clear()
  } catch {
    // Some environments block storage entirely; the shell copes and so does this test.
  }
})

/**
 * The sections used to be a row of pills in the dashboard's header, which made the dashboard the
 * only place navigation existed and left every sub-page a dead end. Two sub-pages had grown their
 * own partial copies of that row to compensate, each offering a different subset.
 */
describe('AdminShell', () => {
  it('reaches every admin section from any section', () => {
    renderShell('/admin/ai-capabilities')

    const nav = screen.getByRole('navigation', { name: 'Admin sections' })
    for (const item of ADMIN_NAV) {
      expect(nav).toHaveTextContent(item.label)
    }
  })

  it('marks the section you are on as the current page', () => {
    renderShell('/admin/default-models')

    const current = screen.getByRole('link', { current: 'page' })
    expect(current).toHaveTextContent('Default models')
  })

  it('collapses to icons only, and remembers the choice', () => {
    renderShell()

    fireEvent.click(screen.getByRole('button', { name: 'Collapse sidebar' }))

    // Labels go, destinations stay — the links are still there to click. Scoped to the nav so
    // AppShell's own brand/sign-in links are not counted.
    expect(screen.queryByText('Workflow policies')).not.toBeInTheDocument()
    const nav = screen.getByRole('navigation', { name: 'Admin sections' })
    expect(within(nav).getAllByRole('link')).toHaveLength(ADMIN_NAV.length)
    expect(localStorage.getItem('ask-lucy.admin-sidebar-collapsed')).toBe('true')

    fireEvent.click(screen.getByRole('button', { name: 'Expand sidebar' }))
    expect(screen.getByText('Workflow policies')).toBeInTheDocument()
  })

  it('renders the section content beside the sidebar', () => {
    renderShell()
    expect(screen.getByText('section content')).toBeInTheDocument()
  })
})
