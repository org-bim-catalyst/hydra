import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { AdminAiProvider } from '../api/adminAiProvidersApi'
import { ProviderHealthCell } from './ProviderHealthCell'

const base: AdminAiProvider = {
  id: 'provider-1',
  providerKey: 'google-gemini',
  displayName: 'Google Gemini',
  isEnabled: true,
  hasCredential: true,
  credentialHint: 'sk-a...ygAA',
  credentialLastRotatedAtUtc: null,
  defaultModelId: null,
  healthStatus: 'Healthy',
  healthStatusCheckedAtUtc: '2026-08-29T09:07:49Z',
  healthFailureKind: null,
  healthFailureReason: null,
  healthStaleAfterUtc: '2026-08-29T09:13:49Z',
}

describe('ProviderHealthCell (specs/043 US2)', () => {
  it('shows a healthy provider as healthy, with when that was confirmed', () => {
    render(<ProviderHealthCell provider={base} />)

    expect(screen.getByText('Healthy')).toBeInTheDocument()
    expect(screen.getByText(/Checked/)).toBeInTheDocument()
  })

  it('distinguishes a quota problem from a credential problem (FR-018)', () => {
    // The whole point of the story: before this, both rendered as an identical red
    // "Unhealthy" chip, so an administrator could not tell "wait" from "go fix the key".
    render(
      <ProviderHealthCell
        provider={{
          ...base,
          healthStatus: 'Unhealthy',
          healthFailureKind: 'QuotaExhausted',
          healthFailureReason: 'Google Gemini is configured correctly, but its usage quota is exhausted.',
        }}
      />,
    )

    expect(screen.getByText(/Configured — temporarily limited/)).toBeInTheDocument()
    expect(screen.getByText(/usage quota is exhausted/)).toBeInTheDocument()
    expect(screen.queryByText('Unhealthy')).not.toBeInTheDocument()
  })

  it.each(['QuotaExhausted', 'RateLimited'] as const)(
    'treats %s as configured-but-limited rather than unhealthy',
    (kind) => {
      render(
        <ProviderHealthCell
          provider={{ ...base, healthStatus: 'Unhealthy', healthFailureKind: kind, healthFailureReason: 'Limited.' }}
        />,
      )

      expect(screen.getByText(/temporarily limited/)).toBeInTheDocument()
    },
  )

  it('shows a rejected credential as unhealthy, with its reason', () => {
    render(
      <ProviderHealthCell
        provider={{
          ...base,
          healthStatus: 'Unhealthy',
          healthFailureKind: 'CredentialRejected',
          healthFailureReason: 'Google Gemini rejected the configured credential.',
        }}
      />,
    )

    expect(screen.getByText('Unhealthy')).toBeInTheDocument()
    expect(screen.getByText(/rejected the configured credential/)).toBeInTheDocument()
  })

  it('shows a never-checked provider as not yet checked, never as an error (FR-020)', () => {
    render(
      <ProviderHealthCell
        provider={{ ...base, healthStatus: 'Unknown', healthStatusCheckedAtUtc: null, healthStaleAfterUtc: null }}
      />,
    )

    expect(screen.getByText('Not yet checked')).toBeInTheDocument()
    expect(screen.queryByText('Unhealthy')).not.toBeInTheDocument()
  })

  it('shows a provider with no credential as not configured (FR-021)', () => {
    render(
      <ProviderHealthCell provider={{ ...base, hasCredential: false, healthStatus: 'Unknown', healthStaleAfterUtc: null }} />,
    )

    expect(screen.getByText('Not configured')).toBeInTheDocument()
  })

  it('does not present a disabled provider as a failure', () => {
    render(
      <ProviderHealthCell
        provider={{ ...base, isEnabled: false, healthStatus: 'Unhealthy', healthFailureKind: 'Unavailable' }}
      />,
    )

    expect(screen.queryByText('Unhealthy')).not.toBeInTheDocument()
    expect(screen.getByText(/Not checked while disabled/)).toBeInTheDocument()
  })

  it('never renders the staleness chip — that now lives in ProviderStalenessCell (specs/062 US1)', () => {
    render(<ProviderHealthCell provider={base} />)

    expect(screen.queryByText('Possibly out of date')).not.toBeInTheDocument()
  })
})
