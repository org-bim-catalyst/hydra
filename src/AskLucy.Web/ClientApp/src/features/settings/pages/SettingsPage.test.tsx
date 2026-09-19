import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { SettingsPage } from './SettingsPage'
import { SETTINGS_TAB_INDEX } from '../settingsTabs'

const server = setupServer(
  http.get('*/api/v1/users/me', () => HttpResponse.json({ email: 'lucy@example.com', firstName: 'Lucy' })),
  http.get('*/api/v1/ai/providers', () => HttpResponse.json([])),
  http.get('*/api/v1/auth/password/status', () => HttpResponse.json({ hasPassword: true })),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderSettings(initialTab?: number) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const entries =
    initialTab === undefined ? ['/settings'] : [{ pathname: '/settings', state: { tab: initialTab } }]
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={entries}>
        <SettingsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('SettingsPage tabs (specs/025-chat-configuration-settings, T006)', () => {
  it('renders only the tabs that still belong here', async () => {
    // Four tabs left this page: "AI Providers" to the admin panel (which model answers a user is
    // a platform decision, configured there as the Chat capability), and Voice / Chat
    // Configuration / Chat History to the Chat settings page, where they sit together instead of
    // beside password changes and cookie preferences.
    renderSettings()
    await screen.findByRole('heading', { name: 'Settings' })

    for (const label of ['Security', 'Account', 'Data', 'Cookies', 'Viewer']) {
      expect(screen.getByRole('tab', { name: label })).toBeInTheDocument()
    }

    for (const moved of ['AI Providers', 'Voice', 'Chat Configuration', 'Chat History']) {
      expect(screen.queryByRole('tab', { name: moved })).not.toBeInTheDocument()
    }
  })

  it('defaults to the Security tab when no location.state.tab is provided', async () => {
    renderSettings()
    await screen.findByRole('heading', { name: 'Settings' })

    expect(screen.getByRole('tab', { name: 'Security' })).toHaveAttribute('aria-selected', 'true')
  })

  it('seeds the initially active tab from location.state.tab', async () => {
    renderSettings(SETTINGS_TAB_INDEX.Data)
    await screen.findByRole('heading', { name: 'Settings' })

    expect(screen.getByRole('tab', { name: 'Data' })).toHaveAttribute('aria-selected', 'true')
  })

  it('keeps every remaining tab on its original index, so saved deep links still land', async () => {
    // The tabs carry explicit values rather than positional indices. Four tabs were removed from
    // the middle of this list — AI Providers to the admin panel, and Voice/Chat Configuration/
    // Chat History to Chat settings. Positional numbering would have shifted Viewer from 8 to 4
    // and silently repointed every SETTINGS_TAB_INDEX consumer.
    renderSettings(SETTINGS_TAB_INDEX.Viewer)
    await screen.findByRole('heading', { name: 'Settings' })

    expect(screen.getByRole('tab', { name: 'Viewer' })).toHaveAttribute('aria-selected', 'true')
  })
})

// specs/058-password-recovery T046/T052. The section has two shapes, and which one renders is the
// server's answer about the account, not a client guess.
describe('SettingsPage password section (specs/058-password-recovery)', () => {
  it('asks for the current password and a confirmation when the account has a password', async () => {
    renderSettings()
    await screen.findByRole('heading', { name: 'Change password' })

    expect(screen.getByLabelText('Current password')).toBeInTheDocument()
    expect(screen.getByLabelText('New password')).toBeInTheDocument()
    expect(screen.getByLabelText('Confirm new password')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Update password' })).toBeInTheDocument()
  })

  it('offers to set a first password, with no current-password field, for an external-only account', async () => {
    server.use(
      http.get('*/api/v1/auth/password/status', () => HttpResponse.json({ hasPassword: false })),
    )

    renderSettings()
    await screen.findByRole('heading', { name: 'Set a password' })

    expect(screen.queryByLabelText('Current password')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Set password' })).toBeInTheDocument()
  })

  it('ticks off each requirement as the new password satisfies it', async () => {
    // FR-022: the policy is one rule set, so it is shown the same way here as on the registration
    // and reset screens rather than as a sentence of prose the user has to parse.
    const user = userEvent.setup()

    renderSettings()
    await screen.findByRole('heading', { name: 'Change password' })

    const newPassword = screen.getByLabelText('New password')
    await user.type(newPassword, 'lowercase')

    // Assert on the visually-hidden state text rather than the icon: it is what a screen reader
    // announces, and it is the only part of the row that changes.
    const ruleRow = (label: string) => within(screen.getByText(label).closest('li')!)

    expect(ruleRow('At least 8 characters').getByText('— met')).toBeInTheDocument()
    expect(ruleRow('A number').getByText('— not met')).toBeInTheDocument()

    await user.type(newPassword, '1')
    expect(ruleRow('A number').getByText('— met')).toBeInTheDocument()
  })

  it('refuses to submit when the two new passwords differ', async () => {
    const user = userEvent.setup()
    let submitted = false
    server.use(
      http.post('*/api/v1/auth/change-password', () => {
        submitted = true
        return new HttpResponse(null, { status: 204 })
      }),
    )

    renderSettings()
    await screen.findByRole('heading', { name: 'Change password' })

    await user.type(screen.getByLabelText('Current password'), 'Current-Passw0rd!')
    await user.type(screen.getByLabelText('New password'), 'Brand-New-Passw0rd!')
    await user.type(screen.getByLabelText('Confirm new password'), 'Brand-New-Passw0rd')
    await user.click(screen.getByRole('button', { name: 'Update password' }))

    expect(await screen.findByText('Both passwords must match.')).toBeInTheDocument()
    expect(submitted).toBe(false)
  })

  it('surfaces each policy rule the server rejected, rather than a generic message', async () => {
    // The password below satisfies every rule the checklist can see, so it reaches the server —
    // which is the point. The server is the authority on the policy and may enforce rules this
    // build knows nothing about, so whatever reason it gives has to reach the user verbatim
    // rather than being flattened into "password rejected".
    const user = userEvent.setup()
    server.use(
      http.post('*/api/v1/auth/change-password', () =>
        HttpResponse.json(
          {
            title: 'Password does not meet requirements',
            status: 400,
            errors: { newPassword: ['Passwords must use at least 6 different characters.'] },
          },
          { status: 400 },
        ),
      ),
    )

    renderSettings()
    await screen.findByRole('heading', { name: 'Change password' })

    await user.type(screen.getByLabelText('Current password'), 'Current-Passw0rd!')
    await user.type(screen.getByLabelText('New password'), 'Aa1!Aa1!Aa1!')
    await user.type(screen.getByLabelText('Confirm new password'), 'Aa1!Aa1!Aa1!')
    await user.click(screen.getByRole('button', { name: 'Update password' }))

    expect(
      await screen.findByText('Passwords must use at least 6 different characters.'),
    ).toBeInTheDocument()
  })

  it('refuses a new password that does not meet the checklist, without asking the server', async () => {
    // FR-022: the same rule set that the checklist displays is also enforced before submitting, so
    // an obviously-failing password gets an instant answer instead of a server round-trip.
    const user = userEvent.setup()
    let submitted = false
    server.use(
      http.post('*/api/v1/auth/change-password', () => {
        submitted = true
        return new HttpResponse(null, { status: 204 })
      }),
    )

    renderSettings()
    await screen.findByRole('heading', { name: 'Change password' })

    await user.type(screen.getByLabelText('Current password'), 'Current-Passw0rd!')
    await user.type(screen.getByLabelText('New password'), 'weakpassword')
    await user.type(screen.getByLabelText('Confirm new password'), 'weakpassword')
    await user.click(screen.getByRole('button', { name: 'Update password' }))

    expect(
      await screen.findByText('This password does not meet every requirement below.'),
    ).toBeInTheDocument()
    expect(submitted).toBe(false)
  })

  it('surfaces a wrong current password as the server explained it', async () => {
    const user = userEvent.setup()
    server.use(
      http.post('*/api/v1/auth/change-password', () =>
        HttpResponse.json({ title: 'Current password is incorrect', status: 400 }, { status: 400 }),
      ),
    )

    renderSettings()
    await screen.findByRole('heading', { name: 'Change password' })

    await user.type(screen.getByLabelText('Current password'), 'not-my-password')
    await user.type(screen.getByLabelText('New password'), 'Brand-New-Passw0rd!')
    await user.type(screen.getByLabelText('Confirm new password'), 'Brand-New-Passw0rd!')
    await user.click(screen.getByRole('button', { name: 'Update password' }))

    expect(await screen.findByText('Current password is incorrect')).toBeInTheDocument()
  })

  it('confirms success and says the other devices were signed out', async () => {
    const user = userEvent.setup()
    server.use(
      http.post('*/api/v1/auth/change-password', () => new HttpResponse(null, { status: 204 })),
    )

    renderSettings()
    await screen.findByRole('heading', { name: 'Change password' })

    await user.type(screen.getByLabelText('Current password'), 'Current-Passw0rd!')
    await user.type(screen.getByLabelText('New password'), 'Brand-New-Passw0rd!')
    await user.type(screen.getByLabelText('Confirm new password'), 'Brand-New-Passw0rd!')
    await user.click(screen.getByRole('button', { name: 'Update password' }))

    expect(
      await screen.findByText('Password changed. Your other devices have been signed out.'),
    ).toBeInTheDocument()
  })

  it('offers a retry when the password status cannot be loaded', async () => {
    server.use(
      http.get('*/api/v1/auth/password/status', () => new HttpResponse(null, { status: 500 })),
    )

    renderSettings()

    expect(await screen.findByText('Could not load your password settings.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })
})

describe('SettingsPage two-factor authentication', () => {
  it('shows a scannable QR code alongside the manual key after enabling 2FA', async () => {
    server.use(http.post('*/api/v1/auth/2fa/enable', () => HttpResponse.json('ABCD1234EFGH5678')))

    const user = userEvent.setup()
    renderSettings()
    await user.click(await screen.findByRole('button', { name: 'Enable 2FA' }))

    expect(await screen.findByText('ABCD1234EFGH5678')).toBeInTheDocument()
    expect(await screen.findByRole('img', { name: 'Scan this QR code with your authenticator app' })).toBeInTheDocument()
  })

  it('offers a markdown download once recovery codes are generated', async () => {
    server.use(
      http.post('*/api/v1/auth/2fa/recovery-codes', () => HttpResponse.json(['CODE1-AAAAA', 'CODE2-BBBBB'])),
    )
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    URL.createObjectURL = vi.fn(() => 'blob:mock')
    URL.revokeObjectURL = vi.fn()

    const user = userEvent.setup()
    renderSettings()
    await user.click(await screen.findByRole('button', { name: 'Generate recovery codes' }))
    await screen.findByText('CODE1-AAAAA')

    await user.click(screen.getByRole('button', { name: 'Download as .md' }))

    expect(clickSpy).toHaveBeenCalled()
    clickSpy.mockRestore()
  })
})
