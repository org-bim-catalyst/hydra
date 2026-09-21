import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { axe, toHaveNoViolations } from 'jest-axe'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { AdminShell } from './AdminShell'

expect.extend(toHaveNoViolations)

vi.mock('../../../hooks/useIsAdmin', () => ({ useIsAdmin: () => true }))
vi.mock('../api/adminHangfireApi', () => ({
  postHangfireSession: vi.fn().mockResolvedValue(undefined),
}))

function renderShell() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/admin/dashboard']}>
        <AdminShell title="Admin Dashboard">
          <div>section content</div>
        </AdminShell>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

// specs/060-hangfire-dashboard-access: the "Jobs" entry is the sidebar's only button (every
// other entry is a RouterLink), so it needs its own keyboard-operability/focus-visibility check
// — the a11y scan below covers ARIA/semantics generally, but not "can this actually be tabbed
// to and clearly focused."
describe('AdminShell accessibility', () => {
  it('has no automatically detectable a11y violations (constitution §10)', async () => {
    const { container } = renderShell()

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('the Jobs entry is keyboard-focusable', () => {
    renderShell()
    const nav = screen.getByRole('navigation', { name: 'Admin sections' })
    const jobsButton = within(nav).getByRole('button', { name: 'Jobs' })

    jobsButton.focus()

    expect(jobsButton).toHaveFocus()
  })

  // specs/062 US5 — the divider grouping "System agents" is a non-interactive <hr>-style
  // element (role="separator", not in the tab sequence), so Tab must skip straight from "Role
  // assignments" to "System agents" without an extra stop.
  it('the divider after System agents introduces no a11y violations and keeps tab order intact', async () => {
    const { container } = renderShell()
    const nav = screen.getByRole('navigation', { name: 'Admin sections' })

    const results = await axe(container)
    expect(results).toHaveNoViolations()

    const roleAssignmentsLink = within(nav).getByRole('link', { name: /Role assignments/ })
    const systemAgentsLink = within(nav).getByRole('link', { name: /System agents/ })

    const user = userEvent.setup()
    roleAssignmentsLink.focus()
    expect(roleAssignmentsLink).toHaveFocus()

    await user.tab()

    expect(systemAgentsLink).toHaveFocus()
  })
})
