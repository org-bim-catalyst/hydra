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
  credentialLastRotatedAtUtc: null,
  defaultModelId: null,
  healthStatus: 'Healthy',
  healthStatusCheckedAtUtc: '2026-08-29T09:07:49Z',
  healthFailureKind: null,
  healthFailureReason: null,
  healthStaleAfterUtc: '2026-08-29T09:13:49Z',
}

const NOW_FRESH = new Date('2026-08-29T09:10:00Z')
const NOW_STALE = new Date('2026-08-31T09:10:00Z')

describe('ProviderHealthCell (specs/043 US2)', () => {
  it('shows a healthy provider as healthy, with when that was confirmed', () => {
    render(<ProviderHealthCell provider={base} now={NOW_FRESH} />)

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
        now={NOW_FRESH}
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
          now={NOW_FRESH}
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
        now={NOW_FRESH}
      />,
    )

    expect(screen.getByText('Unhealthy')).toBeInTheDocument()
    expect(screen.getByText(/rejected the configured credential/)).toBeInTheDocument()
  })

  it('shows a never-checked provider as not yet checked, never as an error (FR-020)', () => {
    render(
      <ProviderHealthCell
        provider={{ ...base, healthStatus: 'Unknown', healthStatusCheckedAtUtc: null, healthStaleAfterUtc: null }}
        now={NOW_FRESH}
      />,
    )

    expect(screen.getByText('Not yet checked')).toBeInTheDocument()
    expect(screen.queryByText('Unhealthy')).not.toBeInTheDocument()
  })

  it('shows a provider with no credential as not configured (FR-021)', () => {
    render(
      <ProviderHealthCell
        provider={{ ...base, hasCredential: false, healthStatus: 'Unknown', healthStaleAfterUtc: null }}
        now={NOW_FRESH}
      />,
    )

    expect(screen.getByText('Not configured')).toBeInTheDocument()
  })

  it('does not present a disabled provider as a failure', () => {
    render(
      <ProviderHealthCell
        provider={{ ...base, isEnabled: false, healthStatus: 'Unhealthy', healthFailureKind: 'Unavailable' }}
        now={NOW_FRESH}
      />,
    )

    expect(screen.queryByText('Unhealthy')).not.toBeInTheDocument()
    expect(screen.getByText(/Not checked while disabled/)).toBeInTheDocument()
  })

  it('flags a result older than its freshness horizon as possibly out of date (FR-019/SC-005)', () => {
    // The reported bug showed a status two days old rendering as current fact.
    render(<ProviderHealthCell provider={base} now={NOW_STALE} />)

    expect(screen.getByText('Possibly out of date')).toBeInTheDocument()
  })

  it('does not flag a result inside its freshness horizon', () => {
    render(<ProviderHealthCell provider={base} now={NOW_FRESH} />)

    expect(screen.queryByText('Possibly out of date')).not.toBeInTheDocument()
  })

  it('never flags a never-checked provider as stale', () => {
    // No horizon means no claim was made, so there is nothing to go out of date.
    render(
      <ProviderHealthCell
        provider={{ ...base, healthStatus: 'Unknown', healthStatusCheckedAtUtc: null, healthStaleAfterUtc: null }}
        now={NOW_STALE}
      />,
    )

    expect(screen.queryByText('Possibly out of date')).not.toBeInTheDocument()
  })
})
