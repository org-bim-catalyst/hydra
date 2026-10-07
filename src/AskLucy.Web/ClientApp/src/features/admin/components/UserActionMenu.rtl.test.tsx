import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import { seedArabic, setTheme, watchI18nFallbacks } from '../../../i18n/testUtils'
import { ApiError } from '../../../api/httpClient'
import type { UserAdmin } from '../api/adminApi'
import * as adminApi from '../api/adminApi'
import { UserActionMenu } from './UserActionMenu'

// Arabic params are wrapped in Unicode isolates (the effect of <bdi>), so names include them.
const iso = (text: string) => `\u2068${text}\u2069`

vi.mock('../api/adminApi', async () => {
  const actual = await vi.importActual<typeof adminApi>('../api/adminApi')
  return {
    ...actual,
    lockUser: vi.fn().mockResolvedValue(undefined),
    deleteUser: vi.fn().mockResolvedValue(undefined),
    sendPasswordReset: vi.fn().mockResolvedValue(undefined),
  }
})

const user: UserAdmin = {
  id: 'user-2',
  email: 'jane@example.com',
  firstName: 'Jane',
  lastName: 'Doe',
  emailConfirmed: false,
  twoFactorEnabled: true,
  lockoutEnabled: true,
  isLockedOut: false,
  role: 'User',
  createdAtUtc: '2026-07-28T00:00:00Z',
}

function renderMenu() {
  const queryClient = new QueryClient()
  seedArabic(queryClient)
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <LocalizedSurface scope="subtree">
          <UserActionMenu user={user} isSelf={false} isSuperUser={false} />
        </LocalizedSurface>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('UserActionMenu in Arabic (T212)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setTheme()
  })

  it('shows Arabic menu items, confirmation dialog and success feedback', async () => {
    const fallbacks = watchI18nFallbacks()
    renderMenu()

    fireEvent.click(screen.getByRole('button', { name: `إجراءات ${iso('jane@example.com')}` }))
    expect(await screen.findByText('قفل الحساب')).toBeInTheDocument()
    expect(screen.getByText('فرض إعادة تعيين 2FA')).toBeInTheDocument()
    expect(screen.getByText('إعادة إرسال رسالة التأكيد')).toBeInTheDocument()
    expect(screen.getByText('حذف الحساب')).toBeInTheDocument()

    fireEvent.click(screen.getByText('إرسال رابط إعادة تعيين كلمة المرور'))
    expect(await screen.findByText(/تم إرسال رابط إعادة تعيين كلمة المرور إلى/)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: `إجراءات ${iso('jane@example.com')}` }))
    fireEvent.click(await screen.findByText('حذف الحساب'))
    expect(await screen.findByText('حذف هذا الحساب؟')).toBeInTheDocument()
    expect(screen.getByText('تأكيد')).toBeInTheDocument()
    expect(screen.getByText('إلغاء')).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('shows the server detail when a lock fails instead of failing silently', async () => {
    vi.mocked(adminApi.lockUser).mockRejectedValueOnce(new ApiError(500, 'x', 'تعذّر قفل الحساب.'))
    renderMenu()

    fireEvent.click(screen.getByRole('button', { name: `إجراءات ${iso('jane@example.com')}` }))
    fireEvent.click(await screen.findByText('قفل الحساب'))
    fireEvent.click(await screen.findByText('تأكيد'))

    await waitFor(() => expect(screen.getByText('تعذّر قفل الحساب.')).toBeInTheDocument())
  })
})
