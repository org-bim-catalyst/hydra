import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AdminShell } from './AdminShell'
import { ADMIN_NAV } from '../adminNav'
import { useIsAdmin } from '../../../hooks/useIsAdmin'

// specs/055-role-management: AdminShell now filters ADMIN_NAV by the caller's built-in-admin
// status/permissions. These tests predate that and assert every section is always reachable —
// mock the built-in-admin check so the filter is a no-op, same as a real Administrator/Super
// User session would produce. Individual tests override this per-case (specs/060-hangfire-
// dashboard-access) to assert the "Jobs" entry's own role gating.
vi.mock('../../../hooks/useIsAdmin', () => ({ useIsAdmin: vi.fn(() => true) }))

vi.mock('../api/adminHangfireApi', () => ({
  postHangfireSession: vi.fn().mockResolvedValue(undefined),
}))

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
  vi.mocked(useIsAdmin).mockReturnValue(true)
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
    // AppShell's own brand/sign-in links are not counted. "Jobs" (specs/060-hangfire-dashboard-
    // access) is action-triggered, not a route, so it renders as a button, not a link — it's
    // excluded from this count rather than inflating it.
    expect(screen.queryByText('Workflow policies')).not.toBeInTheDocument()
    const nav = screen.getByRole('navigation', { name: 'Admin sections' })
    const routableCount = ADMIN_NAV.filter((item) => item.path !== undefined).length
    expect(within(nav).getAllByRole('link')).toHaveLength(routableCount)
    expect(localStorage.getItem('ask-lucy.admin-sidebar-collapsed')).toBe('true')

    fireEvent.click(screen.getByRole('button', { name: 'Expand sidebar' }))
    expect(screen.getByText('Workflow policies')).toBeInTheDocument()
  })

  it('renders the section content beside the sidebar', () => {
    renderShell()
    expect(screen.getByText('section content')).toBeInTheDocument()
  })

  // specs/062 US2 — the sidebar and content pane must stretch to the full viewport height, like
  // the Account Settings page, and the content pane scrolls independently of the sidebar.
  it('stretches the sidebar and content pane to fill the available height', () => {
    renderShell()

    const nav = screen.getByRole('navigation', { name: 'Admin sections' })
    const row = nav.parentElement
    expect(row).toHaveStyle({ alignItems: 'stretch' })

    const content = screen.getByText('section content').parentElement
    expect(content).toHaveStyle({ display: 'flex', flexDirection: 'column', minHeight: 0, flex: '1 1 0%' })
  })

  it('keeps the sidebar stretched to match the content pane when collapsed', () => {
    renderShell()

    fireEvent.click(screen.getByRole('button', { name: 'Collapse sidebar' }))

    const nav = screen.getByRole('navigation', { name: 'Admin sections' })
    const row = nav.parentElement
    expect(row).toHaveStyle({ alignItems: 'stretch' })
  })

  // specs/060-hangfire-dashboard-access: the "Jobs" entry is `builtInOnly` (Administrator/Super
  // User only), same gate as Roles/Role assignments — asserted directly here since, unlike those,
  // it renders as a button rather than a link and so isn't covered by the link-count assertion
  // above.
  it('shows the Jobs entry for a built-in admin, and hides it otherwise', () => {
    renderShell()
    const nav = screen.getByRole('navigation', { name: 'Admin sections' })
    expect(within(nav).getByRole('button', { name: 'Jobs' })).toBeInTheDocument()

    vi.mocked(useIsAdmin).mockReturnValue(false)
    renderShell()
    const navs = screen.getAllByRole('navigation', { name: 'Admin sections' })
    expect(within(navs[navs.length - 1]).queryByText('Jobs')).not.toBeInTheDocument()
  })

  // specs/062 US5 — "System agents" sits right after "Role assignments", set off from the rest
  // of the nav by a single divider.
  it('orders System agents right after Role assignments, separated by one divider', () => {
    renderShell()

    const nav = screen.getByRole('navigation', { name: 'Admin sections' })
    const labels = ADMIN_NAV.map((item) => item.label)
    const roleAssignmentsIndex = labels.indexOf('Role assignments')
    expect(labels[roleAssignmentsIndex + 1]).toBe('System agents')

    // The sidebar's own header divider (above the list) plus this one grouping divider — two
    // total, not one — so scope the assertion to the <List> itself. The divider row is its own
    // <li> wrapping an <hr> (an <hr> can't be a direct sibling of the row <li>s in a <ul> once it
    // carries role="separator" — see AdminShell.tsx), so a divider row is identified by having no
    // link/button of its own.
    const list = nav.querySelector('ul')
    const children = Array.from(list?.children ?? [])
    const dividerRows = children.filter((child) => child.querySelector('.MuiDivider-root'))
    expect(dividerRows).toHaveLength(1)

    const dividerIndex = children.indexOf(dividerRows[0])
    const systemAgentsChildIndex = children.findIndex((child) => child.textContent?.includes('System agents'))
    expect(dividerIndex).toBe(systemAgentsChildIndex + 1)
  })

  it('mints a dashboard session and opens a new tab when Jobs is clicked', async () => {
    const fakeTab = { location: { href: '' }, close: vi.fn() }
    vi.spyOn(window, 'open').mockReturnValue(fakeTab as unknown as Window)
    renderShell()

    const nav = screen.getByRole('navigation', { name: 'Admin sections' })
    fireEvent.click(within(nav).getByRole('button', { name: 'Jobs' }))

    expect(window.open).toHaveBeenCalledWith('', '_blank')
    await waitFor(() => expect(fakeTab.location.href).toBe('/hangfire'))
  })
})
