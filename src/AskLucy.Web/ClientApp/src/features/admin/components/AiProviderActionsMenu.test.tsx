import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AdminAiProvider } from '../api/adminAiProvidersApi'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { ApiError } from '../../../api/httpClient'
import { AiProviderActionsMenu } from './AiProviderActionsMenu'

// Same reasoning as UserActionMenu.test.tsx: MUI's Paper-based Popper/Dialog surfaces
// render an inline `--Paper-shadow` custom property that jsdom's CSS length parser cannot
// resolve, which crashes testing-library's role-based accessibility check for any element
// inside that subtree. Interactions below use fireEvent + text/DOM queries instead of
// getByRole for anything rendered inside a Dialog.
vi.mock('../api/adminAiProvidersApi', async () => {
  const actual = await vi.importActual<typeof adminAiProvidersApi>('../api/adminAiProvidersApi')
  return {
    ...actual,
    updateProvider: vi.fn().mockResolvedValue(undefined),
    setCredential: vi.fn().mockResolvedValue(undefined),
    clearCredential: vi.fn().mockResolvedValue(undefined),
    checkProviderHealth: vi.fn(),
  }
})

const disabledNoCredential: AdminAiProvider = {
  id: 'provider-1',
  providerKey: 'anthropic',
  displayName: 'Anthropic',
  isEnabled: false,
  hasCredential: false,
  credentialHint: null,
  credentialLastRotatedAtUtc: null,
  defaultModelId: null,
  healthStatus: 'Unknown',
  healthStatusCheckedAtUtc: null,
  healthFailureKind: null,
  healthFailureReason: null,
  healthStaleAfterUtc: null,
}

const disabledWithCredential: AdminAiProvider = {
  ...disabledNoCredential,
  hasCredential: true,
}

const enabledWithCredential: AdminAiProvider = {
  ...disabledWithCredential,
  isEnabled: true,
}

function renderMenu(provider: AdminAiProvider) {
  const queryClient = new QueryClient()
  return render(
    <QueryClientProvider client={queryClient}>
      <AiProviderActionsMenu provider={provider} />
    </QueryClientProvider>,
  )
}

describe('AiProviderActionsMenu', () => {
  beforeEach(() => vi.clearAllMocks())

  it('opens a dialog to set a credential; Cancel does not call the API', async () => {
    renderMenu(disabledNoCredential)

    fireEvent.click(screen.getByRole('button', { name: /set credential for anthropic/i }))

    expect(await screen.findByText('Set credential for Anthropic')).toBeInTheDocument()
    fireEvent.click(screen.getByText('Cancel'))

    expect(adminAiProvidersApi.setCredential).not.toHaveBeenCalled()
  })

  it('submits the typed credential on Confirm, and never renders it again afterward', async () => {
    renderMenu(disabledNoCredential)

    fireEvent.click(screen.getByRole('button', { name: /set credential for anthropic/i }))

    const input = await screen.findByLabelText('API key')
    fireEvent.change(input, { target: { value: 'sk-super-secret-value' } })
    fireEvent.click(screen.getByText('Confirm'))

    await waitFor(() => expect(adminAiProvidersApi.setCredential).toHaveBeenCalledWith('provider-1', 'sk-super-secret-value'))
    expect(screen.queryByText('sk-super-secret-value')).not.toBeInTheDocument()
    expect(screen.queryByDisplayValue('sk-super-secret-value')).not.toBeInTheDocument()
  })

  it('rejects an empty credential without calling the API', async () => {
    renderMenu(disabledNoCredential)

    fireEvent.click(screen.getByRole('button', { name: /set credential for anthropic/i }))
    fireEvent.click(screen.getByText('Confirm'))

    expect(adminAiProvidersApi.setCredential).not.toHaveBeenCalled()
  })

  it('shows a "needs a credential" explanation immediately when Enable is clicked with no credential configured — no API call, no confirmation dialog (FR-003)', async () => {
    renderMenu(disabledNoCredential)

    fireEvent.click(screen.getByRole('button', { name: /enable anthropic/i }))

    expect(await screen.findByText(/needs a credential/i)).toBeInTheDocument()
    expect(screen.queryByText('Confirm')).not.toBeInTheDocument()
    expect(adminAiProvidersApi.updateProvider).not.toHaveBeenCalled()
  })

  it('opens a confirm dialog for Enable when a credential is already configured, and calls the API only on Confirm', async () => {
    renderMenu(disabledWithCredential)

    fireEvent.click(screen.getByRole('button', { name: /enable anthropic/i }))

    expect(await screen.findByText('Enable this provider?')).toBeInTheDocument()
    expect(adminAiProvidersApi.updateProvider).not.toHaveBeenCalled()

    fireEvent.click(screen.getByText('Confirm'))

    await waitFor(() => expect(adminAiProvidersApi.updateProvider).toHaveBeenCalledWith('provider-1', { isEnabled: true }))
  })

  it('does not call the API if the Enable confirmation is cancelled', async () => {
    renderMenu(disabledWithCredential)

    fireEvent.click(screen.getByRole('button', { name: /enable anthropic/i }))
    fireEvent.click(screen.getByText('Cancel'))

    expect(adminAiProvidersApi.updateProvider).not.toHaveBeenCalled()
  })

  it('shows a confirm dialog for Disable, and calls updateProvider(isEnabled: false) only on Confirm', async () => {
    renderMenu(enabledWithCredential)

    fireEvent.click(screen.getByRole('button', { name: /disable anthropic/i }))
    expect(await screen.findByText('Disable this provider?')).toBeInTheDocument()
    expect(adminAiProvidersApi.updateProvider).not.toHaveBeenCalled()

    fireEvent.click(screen.getByText('Confirm'))

    await waitFor(() => expect(adminAiProvidersApi.updateProvider).toHaveBeenCalledWith('provider-1', { isEnabled: false }))
  })

  it("clearing a credential's confirmation explicitly states it will also disable the provider, and Cancel does not call the API", async () => {
    renderMenu(enabledWithCredential)

    fireEvent.click(screen.getByRole('button', { name: /clear credential for anthropic/i }))

    expect(await screen.findByText(/will also disable/i)).toBeInTheDocument()
    fireEvent.click(screen.getByText('Cancel'))

    expect(adminAiProvidersApi.clearCredential).not.toHaveBeenCalled()
  })

  it('calls clearCredential only on Confirm', async () => {
    renderMenu(enabledWithCredential)

    fireEvent.click(screen.getByRole('button', { name: /clear credential for anthropic/i }))
    fireEvent.click(screen.getByText('Confirm'))

    await waitFor(() => expect(adminAiProvidersApi.clearCredential).toHaveBeenCalledWith('provider-1'))
  })

  it('disables the "Clear credential" icon button when there is no credential to clear', () => {
    renderMenu(disabledNoCredential)

    expect(screen.getByRole('button', { name: /clear credential for anthropic/i })).toBeDisabled()
  })

  it('offers "Replace credential" (reusing the same dialog) instead of "Set credential" when a credential already exists', () => {
    renderMenu(disabledWithCredential)

    expect(screen.getByRole('button', { name: /replace credential for anthropic/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /set credential for anthropic/i })).not.toBeInTheDocument()
  })

  it('shows placeholder text and no pre-filled value when replacing an existing credential (spec 065 US2)', async () => {
    renderMenu(disabledWithCredential)

    fireEvent.click(screen.getByRole('button', { name: /replace credential for anthropic/i }))

    const input = await screen.findByLabelText('API key')
    expect(input).toHaveValue('')
    expect(input).toHaveAttribute('placeholder', 'Please insert API key here')
  })

  it('hides the show/hide toggle when the API key field is empty, and reveals/re-hides typed input without changing its value (spec 065 US1)', async () => {
    renderMenu(disabledNoCredential)

    fireEvent.click(screen.getByRole('button', { name: /set credential for anthropic/i }))

    const input = await screen.findByLabelText('API key')
    expect(screen.queryByLabelText('Show API key')).not.toBeInTheDocument()

    fireEvent.change(input, { target: { value: 'sk-super-secret-value' } })
    expect(input).toHaveAttribute('type', 'password')
    const showToggle = await screen.findByLabelText('Show API key')

    fireEvent.click(showToggle)
    expect(input).toHaveAttribute('type', 'text')
    expect(input).toHaveValue('sk-super-secret-value')
    const hideToggle = await screen.findByLabelText('Hide API key')

    fireEvent.click(hideToggle)
    expect(input).toHaveAttribute('type', 'password')
    expect(input).toHaveValue('sk-super-secret-value')

    fireEvent.change(input, { target: { value: '' } })
    expect(screen.queryByLabelText('Show API key')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Hide API key')).not.toBeInTheDocument()
  })

  it('resets the reveal toggle and masking back to their defaults when the dialog is closed and reopened (spec 065 FR-010)', async () => {
    renderMenu(disabledNoCredential)

    fireEvent.click(screen.getByRole('button', { name: /set credential for anthropic/i }))

    const firstInput = await screen.findByLabelText('API key')
    fireEvent.change(firstInput, { target: { value: 'sk-super-secret-value' } })
    fireEvent.click(await screen.findByLabelText('Show API key'))
    expect(firstInput).toHaveAttribute('type', 'text')

    fireEvent.click(screen.getByText('Cancel'))
    await waitFor(() => expect(screen.queryByText('Set credential for Anthropic')).not.toBeInTheDocument())

    fireEvent.click(screen.getByLabelText(/set credential for anthropic/i))

    const reopenedInput = await screen.findByLabelText('API key')
    expect(reopenedInput).toHaveValue('')
    expect(reopenedInput).toHaveAttribute('type', 'password')
    expect(screen.queryByLabelText('Show API key')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Hide API key')).not.toBeInTheDocument()
  })
})

describe('Check now (specs/043 US3)', () => {
  it('reports a healthy result after a successful probe', async () => {
    vi.mocked(adminAiProvidersApi.checkProviderHealth).mockResolvedValue({
      healthStatus: 'Healthy',
      healthFailureKind: null,
      healthFailureReason: null,
      checkedAtUtc: '2026-08-29T14:22:10Z',
      healthStaleAfterUtc: '2026-08-29T14:28:10Z',
    })
    renderMenu(enabledWithCredential)

    fireEvent.click(screen.getByRole('button', { name: /check now for anthropic/i }))

    await waitFor(() => expect(screen.getByText(/is healthy/)).toBeInTheDocument())
    expect(adminAiProvidersApi.checkProviderHealth).toHaveBeenCalledWith(enabledWithCredential.id)
  })

  it('surfaces the classified reason when the probe finds the provider still failing', async () => {
    vi.mocked(adminAiProvidersApi.checkProviderHealth).mockResolvedValue({
      healthStatus: 'Unhealthy',
      healthFailureKind: 'UsageRestricted',
      healthFailureReason: 'Billing may be disabled for the project. The credential itself is valid.',
      checkedAtUtc: '2026-08-29T14:22:10Z',
      healthStaleAfterUtc: '2026-08-29T14:28:10Z',
    })
    renderMenu(enabledWithCredential)

    fireEvent.click(screen.getByRole('button', { name: /check now for anthropic/i }))

    await waitFor(() => expect(screen.getByText(/Billing may be disabled/)).toBeInTheDocument())
    // The misdirection this feature removes: never tell an administrator to check a key that
    // is demonstrably fine.
    expect(screen.queryByText(/API key/i)).not.toBeInTheDocument()
  })

  it('surfaces a failed probe request rather than failing silently (constitution VIII)', async () => {
    vi.mocked(adminAiProvidersApi.checkProviderHealth).mockRejectedValue(
      new ApiError(500, 'Server error', 'Something broke.'),
    )
    renderMenu(enabledWithCredential)

    fireEvent.click(screen.getByRole('button', { name: /check now for anthropic/i }))

    await waitFor(() => expect(screen.getByText('Something broke.')).toBeInTheDocument())
  })

  it('is unavailable for a provider with no credential to check', () => {
    renderMenu(disabledNoCredential)

    expect(screen.getByRole('button', { name: /check now for anthropic/i })).toBeDisabled()
  })
})
