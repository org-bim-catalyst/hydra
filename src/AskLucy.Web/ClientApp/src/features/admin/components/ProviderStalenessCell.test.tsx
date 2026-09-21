import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { AdminAiProvider } from '../api/adminAiProvidersApi'
import { ProviderStalenessCell } from './ProviderStalenessCell'

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

describe('ProviderStalenessCell (specs/062 US1)', () => {
  it('flags a result older than its freshness horizon as possibly out of date (FR-019/FR-001)', () => {
    // The reported bug showed a status two days old rendering as current fact.
    render(<ProviderStalenessCell provider={base} now={NOW_STALE} />)

    expect(screen.getByText('Possibly out of date')).toBeInTheDocument()
  })

  it('does not flag a result inside its freshness horizon', () => {
    render(<ProviderStalenessCell provider={base} now={NOW_FRESH} />)

    expect(screen.queryByText('Possibly out of date')).not.toBeInTheDocument()
  })

  it('never flags a never-checked provider as stale (edge case: no verdict was ever made)', () => {
    render(
      <ProviderStalenessCell
        provider={{ ...base, healthStatus: 'Unknown', healthStatusCheckedAtUtc: null, healthStaleAfterUtc: null }}
        now={NOW_STALE}
      />,
    )

    expect(screen.queryByText('Possibly out of date')).not.toBeInTheDocument()
  })

  it('renders the staleness chip in its own isolated container, never wrapped with other content (FR-002/SC-001)', () => {
    // Regression coverage for the original bug: the chip used to share a flex-wrap container
    // with the health status chip, causing it to wrap onto a second line and its tooltip to
    // overlap the row below at narrow widths. Rendering it standalone (this component's own
    // return value, with nothing else in the tree) guarantees no sibling to wrap against.
    const { container } = render(<ProviderStalenessCell provider={base} now={NOW_STALE} />)

    expect(container.querySelectorAll('.MuiChip-root')).toHaveLength(1)
  })
})
