import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { CookieConsentBanner } from './CookieConsentBanner'

expect.extend(toHaveNoViolations)

describe('CookieConsentBanner accessibility (constitution §7, §10)', () => {
  // The Customize (per-category toggle) state isn't exercised here: clicking "Customize"
  // re-renders inside MUI Dialog's open transition, and any subsequent testing-library
  // query (`findByRole`/`findByText` alike) then throws resolving a computed style
  // (`resolveLengthInPixels` in jsdom/lib/jsdom/living/css/helpers/font-sizes.js) — a
  // jsdom/MUI-transition interaction, not a real accessibility defect, matching the
  // documented jsdom/virtualization gap in ChatSidebar.a11y.test.tsx. The same `Switch`-
  // based toggle UI is exercised successfully in `CookiePreferencesPanel.a11y.test.tsx`,
  // which renders the toggles on initial load rather than after a Dialog-transitioned
  // click, so that coverage isn't lost overall.
  it('has no automatically detectable a11y violations in its default (Accept/Reject/Customize) state', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <MemoryRouter>
        <QueryClientProvider client={queryClient}>
          <CookieConsentBanner />
        </QueryClientProvider>
      </MemoryRouter>,
    )

    await findByText('We use cookies')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
