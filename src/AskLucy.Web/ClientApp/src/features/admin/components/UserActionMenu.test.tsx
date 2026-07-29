import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { UserAdmin } from '../api/adminApi'
import * as adminApi from '../api/adminApi'
import { UserActionMenu } from './UserActionMenu'

// MUI's Paper-based Popper/Dialog surfaces render an inline `--Paper-shadow` custom
// property that jsdom's CSS length parser cannot resolve, which crashes
// testing-library's role-based accessibility check (isSubtreeInaccessible) for any
// element inside that subtree. Interactions below use fireEvent + text/DOM queries
// instead of getByRole for anything rendered inside a Menu/Dialog, to avoid that crash.
vi.mock('../api/adminApi', async () => {
  const actual = await vi.importActual<typeof adminApi>('../api/adminApi')
  return {
    ...actual,
    lockUser: vi.fn().mockResolvedValue(undefined),
    unlockUser: vi.fn().mockResolvedValue(undefined),
    changeUserRole: vi.fn().mockResolvedValue(undefined),
    forceReset2fa: vi.fn().mockResolvedValue(undefined),
    deleteUser: vi.fn().mockResolvedValue(undefined),
  }
})

const user: UserAdmin = {
  id: 'user-2',
  email: 'jane@example.com',
  firstName: 'Jane',
  lastName: 'Doe',
  emailConfirmed: true,
  twoFactorEnabled: true,
  lockoutEnabled: true,
  isLockedOut: false,
  role: 'Regular',
  createdAtUtc: '2026-07-28T00:00:00Z',
}

function renderMenu(props: Partial<React.ComponentProps<typeof UserActionMenu>> = {}) {
  const queryClient = new QueryClient()
  return render(
    <QueryClientProvider client={queryClient}>
      <UserActionMenu user={user} isSelf={false} isSuperUser={false} {...props} />
    </QueryClientProvider>,
  )
}

describe('UserActionMenu', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows a confirmation dialog before locking, and calls lockUser only on confirm', async () => {
    renderMenu()

    fireEvent.click(screen.getByRole('button', { name: /actions for jane@example.com/i }))
    fireEvent.click(await screen.findByText('Lock account'))

    expect(await screen.findByText('Lock this account?')).toBeInTheDocument()
    expect(adminApi.lockUser).not.toHaveBeenCalled()

    fireEvent.click(screen.getByText('Confirm'))

    await waitFor(() => expect(adminApi.lockUser).toHaveBeenCalledWith('user-2'))
  })

  it('does not call anything if the confirmation dialog is cancelled', async () => {
    renderMenu()

    fireEvent.click(screen.getByRole('button', { name: /actions for jane@example.com/i }))
    fireEvent.click(await screen.findByText('Delete account'))
    expect(await screen.findByText('Delete this account?')).toBeInTheDocument()

    fireEvent.click(screen.getByText('Cancel'))

    expect(adminApi.deleteUser).not.toHaveBeenCalled()
  })

  it('disables self-targeting destructive actions', async () => {
    renderMenu({ isSelf: true })

    fireEvent.click(screen.getByRole('button', { name: /actions for jane@example.com/i }))

    const lockItem = (await screen.findByText('Lock account')).closest('li')
    const force2faItem = screen.getByText('Force 2FA reset').closest('li')
    const deleteItem = screen.getByText('Delete account').closest('li')

    expect(lockItem).toHaveAttribute('aria-disabled', 'true')
    expect(force2faItem).toHaveAttribute('aria-disabled', 'true')
    expect(deleteItem).toHaveAttribute('aria-disabled', 'true')
  })

  it('does not disable actions for a non-self target', async () => {
    renderMenu({ isSelf: false })

    fireEvent.click(screen.getByRole('button', { name: /actions for jane@example.com/i }))

    const lockItem = (await screen.findByText('Lock account')).closest('li')
    expect(lockItem).not.toHaveAttribute('aria-disabled', 'true')
  })

  it('disables granting Administrator/Super User for a plain Administrator caller', async () => {
    renderMenu({ isSuperUser: false })

    fireEvent.click(screen.getByRole('button', { name: /actions for jane@example.com/i }))
    fireEvent.click(await screen.findByText('Change role…'))

    const combobox = document.querySelector('[role="combobox"]');
    expect(combobox).not.toBeNull()
    fireEvent.mouseDown(combobox!)

    const options = await screen.findAllByText(/^(Regular|Administrator|Super User)$/)
    const adminOption = options.find((el) => el.textContent === 'Administrator')!.closest('li')
    const superUserOption = options.find((el) => el.textContent === 'Super User')!.closest('li')

    expect(adminOption).toHaveAttribute('aria-disabled', 'true')
    expect(superUserOption).toHaveAttribute('aria-disabled', 'true')
  })
})
