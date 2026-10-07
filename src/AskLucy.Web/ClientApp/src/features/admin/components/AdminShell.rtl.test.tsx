import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useIsAdmin } from '../../../hooks/useIsAdmin'
import {
  seedArabic,
  setTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../i18n/testUtils'
import { getSummary } from '../api/adminOperationalFailuresApi'
import { ADMIN_NAV } from '../adminNav'
import { AdminShell } from './AdminShell'

expect.extend(toHaveNoViolations)

vi.mock('../../../hooks/useIsAdmin', () => ({ useIsAdmin: vi.fn(() => true) }))
vi.mock('../api/adminHangfireApi', () => ({
  postHangfireSession: vi.fn().mockResolvedValue(undefined),
}))
vi.mock('../api/adminOperationalFailuresApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/adminOperationalFailuresApi')>()),
  getSummary: vi.fn(),
}))

function renderShell() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  seedArabic(queryClient)
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

beforeEach(() => {
  vi.mocked(useIsAdmin).mockReturnValue(true)
  vi.mocked(getSummary).mockReset().mockResolvedValue({ unacknowledgedCriticalRootCauses: 0 })
  setTheme()
  document.documentElement.removeAttribute('dir')
  document.documentElement.removeAttribute('lang')
  try {
    localStorage.clear()
  } catch {
    // Some environments block storage; the shell copes and so does this test.
  }
})
afterEach(() => signOutAndResetTheme())

describe('AdminShell in Arabic (T207)', () => {
  it('sets <html lang dir> to ar/rtl while mounted and restores it on unmount', () => {
    document.documentElement.setAttribute('lang', 'en')
    document.documentElement.setAttribute('dir', 'ltr')

    const { unmount } = renderShell()
    expect(document.documentElement).toHaveAttribute('lang', 'ar')
    expect(document.documentElement).toHaveAttribute('dir', 'rtl')

    unmount()
    expect(document.documentElement).toHaveAttribute('lang', 'en')
    expect(document.documentElement).toHaveAttribute('dir', 'ltr')
  })

  it('shows every sidebar section in Arabic, with no catalog fallbacks', () => {
    const fallbacks = watchI18nFallbacks()
    renderShell()

    const nav = screen.getByRole('navigation', { name: 'أقسام الإدارة' })
    expect(nav).toBeInTheDocument()
    expect(screen.getByText('الإدارة')).toBeInTheDocument()
    for (const item of ADMIN_NAV) {
      // The English label never appears in the sidebar; the Arabic one is rendered for every section.
      expect(nav).not.toHaveTextContent(new RegExp(`^${item.label}$`))
    }
    for (const label of [
      'لوحة التحكم',
      'المستخدمون',
      'الأدوار',
      'خوادم MCP',
      'الإشعارات',
      'القوالب',
      'اللغات',
      'المهام',
    ]) {
      expect(screen.getByText(label)).toBeInTheDocument()
    }
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('mirrors the collapse arrow, and swaps its label with the state', () => {
    renderShell()

    const collapse = screen.getByRole('button', { name: 'طي الشريط الجانبي' })
    expect(collapse).toHaveAttribute('aria-expanded', 'true')
    const icon = collapse.querySelector('svg') as SVGElement
    expect(getComputedStyle(icon).transform).toContain('-1')

    fireEvent.click(collapse)
    expect(screen.getByRole('button', { name: 'توسيع الشريط الجانبي' })).toHaveAttribute(
      'aria-expanded',
      'false',
    )
  })

  it.each(THEME_MODES)('has no axe violations in %s mode', async (mode) => {
    setTheme(mode)
    const { container } = renderShell()

    expect(await axe(container)).toHaveNoViolations()
  })
})
