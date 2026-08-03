import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import * as voiceApi from '../../chat/api/voiceApi'
import { VoiceFailoverPanel } from './VoiceFailoverPanel'

vi.mock('../../chat/api/voiceApi')

function renderPanel() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <VoiceFailoverPanel />
    </QueryClientProvider>,
  )
}

describe('VoiceFailoverPanel', () => {
  it('shows a healthy status and an empty-state message when there are no failovers', async () => {
    vi.mocked(voiceApi.getVoiceProviderHealth).mockResolvedValue({
      currentStatus: 'healthy',
      failoverCount: 0,
      recoveryCount: 0,
      events: [],
    })

    renderPanel()

    expect(await screen.findByText('Healthy')).toBeInTheDocument()
    expect(await screen.findByText(/no failovers in the last 24 hours/i)).toBeInTheDocument()
  })

  it('lists failover/recovery events and flags a degraded status', async () => {
    vi.mocked(voiceApi.getVoiceProviderHealth).mockResolvedValue({
      currentStatus: 'degraded',
      failoverCount: 1,
      recoveryCount: 0,
      events: [
        { occurredAtUtc: '2026-08-02T09:41:12Z', direction: 'FailedOverToFallback', reason: 'stt timeout' },
      ],
    })

    renderPanel()

    expect(await screen.findByText('Degraded')).toBeInTheDocument()
    expect(await screen.findByText('Failed over to fallback')).toBeInTheDocument()
    expect(screen.getByText('stt timeout')).toBeInTheDocument()
  })
})
