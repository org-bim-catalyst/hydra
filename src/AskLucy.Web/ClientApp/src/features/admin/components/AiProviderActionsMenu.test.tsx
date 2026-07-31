import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AdminAiProvider } from '../api/adminAiProvidersApi'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { AiProviderActionsMenu } from './AiProviderActionsMenu'

// Same reasoning as UserActionMenu.test.tsx: MUI's Paper-based Popper/Dialog surfaces
// render an inline `--Paper-shadow` custom property that jsdom's CSS length parser cannot
// resolve, which crashes testing-library's role-based accessibility check for any element
// inside that subtree. Interactions below use fireEvent + text/DOM queries instead of
// getByRole for anything rendered inside a Menu/Dialog.
vi.mock('../api/adminAiProvidersApi', async () => {
  const actual = await vi.importActual<typeof adminAiProvidersApi>('../api/adminAiProvidersApi')
  return {
    ...actual,
    updateProvider: vi.fn().mockResolvedValue(undefined),
    setCredential: vi.fn().mockResolvedValue(undefined),
    clearCredential: vi.fn().mockResolvedValue(undefined),
  }
})

const disabledNoCredential: AdminAiProvider = {
  id: 'provider-1',
  providerKey: 'anthropic',
  displayName: 'Anthropic',
  isEnabled: false,
  hasCredential: false,
  credentialLastRotatedAtUtc: null,
  defaultModelId: null,
  healthStatus: 'Unknown',
  healthStatusCheckedAtUtc: null,
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

    fireEvent.click(screen.getByRole('button', { name: /actions for anthropic/i }))
    fireEvent.click(await screen.findByText('Set credential'))

    expect(await screen.findByText('Set credential for Anthropic')).toBeInTheDocument()
    fireEvent.click(screen.getByText('Cancel'))

    expect(adminAiProvidersApi.setCredential).not.toHaveBeenCalled()
  })

  it('submits the typed credential on Confirm, and never renders it again afterward', async () => {
    renderMenu(disabledNoCredential)

    fireEvent.click(screen.getByRole('button', { name: /actions for anthropic/i }))
    fireEvent.click(await screen.findByText('Set credential'))

    const input = await screen.findByLabelText('API key')
    fireEvent.change(input, { target: { value: 'sk-super-secret-value' } })
    fireEvent.click(screen.getByText('Confirm'))

    await waitFor(() => expect(adminAiProvidersApi.setCredential).toHaveBeenCalledWith('provider-1', 'sk-super-secret-value'))
    expect(screen.queryByText('sk-super-secret-value')).not.toBeInTheDocument()
    expect(screen.queryByDisplayValue('sk-super-secret-value')).not.toBeInTheDocument()
  })

  it('rejects an empty credential without calling the API', async () => {
    renderMenu(disabledNoCredential)

    fireEvent.click(screen.getByRole('button', { name: /actions for anthropic/i }))
    fireEvent.click(await screen.findByText('Set credential'))
    fireEvent.click(screen.getByText('Confirm'))

    expect(adminAiProvidersApi.setCredential).not.toHaveBeenCalled()
  })

  it('shows a "needs a credential" explanation immediately when Enable is clicked with no credential configured — no API call, no confirmation dialog (FR-003)', async () => {
    renderMenu(disabledNoCredential)

    fireEvent.click(screen.getByRole('button', { name: /actions for anthropic/i }))
    fireEvent.click(await screen.findByText('Enable'))

    expect(await screen.findByText(/needs a credential/i)).toBeInTheDocument()
    expect(screen.queryByText('Confirm')).not.toBeInTheDocument()
    expect(adminAiProvidersApi.updateProvider).not.toHaveBeenCalled()
  })

  it('opens a confirm dialog for Enable when a credential is already configured, and calls the API only on Confirm', async () => {
    renderMenu(disabledWithCredential)

    fireEvent.click(screen.getByRole('button', { name: /actions for anthropic/i }))
    fireEvent.click(await screen.findByText('Enable'))

    expect(await screen.findByText('Enable this provider?')).toBeInTheDocument()
    expect(adminAiProvidersApi.updateProvider).not.toHaveBeenCalled()

    fireEvent.click(screen.getByText('Confirm'))

    await waitFor(() => expect(adminAiProvidersApi.updateProvider).toHaveBeenCalledWith('provider-1', { isEnabled: true }))
  })

  it('does not call the API if the Enable confirmation is cancelled', async () => {
    renderMenu(disabledWithCredential)

    fireEvent.click(screen.getByRole('button', { name: /actions for anthropic/i }))
    fireEvent.click(await screen.findByText('Enable'))
    fireEvent.click(screen.getByText('Cancel'))

    expect(adminAiProvidersApi.updateProvider).not.toHaveBeenCalled()
  })

  it('shows a confirm dialog for Disable, and calls updateProvider(isEnabled: false) only on Confirm', async () => {
    renderMenu(enabledWithCredential)

    fireEvent.click(screen.getByRole('button', { name: /actions for anthropic/i }))
    fireEvent.click(await screen.findByText('Disable'))
    expect(await screen.findByText('Disable this provider?')).toBeInTheDocument()
    expect(adminAiProvidersApi.updateProvider).not.toHaveBeenCalled()

    fireEvent.click(screen.getByText('Confirm'))

    await waitFor(() => expect(adminAiProvidersApi.updateProvider).toHaveBeenCalledWith('provider-1', { isEnabled: false }))
  })

  it("clearing a credential's confirmation explicitly states it will also disable the provider, and Cancel does not call the API", async () => {
    renderMenu(enabledWithCredential)

    fireEvent.click(screen.getByRole('button', { name: /actions for anthropic/i }))
    fireEvent.click(await screen.findByText('Clear credential'))

    expect(await screen.findByText(/will also disable/i)).toBeInTheDocument()
    fireEvent.click(screen.getByText('Cancel'))

    expect(adminAiProvidersApi.clearCredential).not.toHaveBeenCalled()
  })

  it('calls clearCredential only on Confirm', async () => {
    renderMenu(enabledWithCredential)

    fireEvent.click(screen.getByRole('button', { name: /actions for anthropic/i }))
    fireEvent.click(await screen.findByText('Clear credential'))
    fireEvent.click(screen.getByText('Confirm'))

    await waitFor(() => expect(adminAiProvidersApi.clearCredential).toHaveBeenCalledWith('provider-1'))
  })

  it('disables the "Clear credential" item when there is no credential to clear', async () => {
    renderMenu(disabledNoCredential)

    fireEvent.click(screen.getByRole('button', { name: /actions for anthropic/i }))
    const clearItem = (await screen.findByText('Clear credential')).closest('li')
    expect(clearItem).toHaveAttribute('aria-disabled', 'true')
  })

  it('offers "Replace credential" (reusing the same dialog) instead of "Set credential" when a credential already exists', async () => {
    renderMenu(disabledWithCredential)

    fireEvent.click(screen.getByRole('button', { name: /actions for anthropic/i }))
    expect(await screen.findByText('Replace credential')).toBeInTheDocument()
    expect(screen.queryByText('Set credential')).not.toBeInTheDocument()
  })
})
