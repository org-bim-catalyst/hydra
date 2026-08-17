import { expect, test } from '@playwright/test'

/**
 * specs/028-ai-floating-panels quickstart.md — Scenarios 1 onward.
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: requires a running backend, frontend dev server, and an
 * authenticated session — see ImmersiveViewerPlatform.spec.ts's doc comment for the same caveat.
 * Run via `npm test` from this directory against a real deployment (`E2E_BASE_URL` env var).
 *
 * Devtools access mirrors spec 027's `window.__askLucyViewerEngine` pattern (US1 uses
 * `window.__askLucyFloatingPanelStore`/`__askLucyPanelTypeRegistry`, development builds only) so
 * this suite doesn't depend on a live AI agent turn being wired up — per spec Assumption, that
 * decision step is out of this feature's scope.
 */

interface FloatingPanelDevtools {
  __askLucyFloatingPanelStore: {
    getState: () => {
      panels: { id: string; title: string; validationStatus: string }[]
      openPanel: (request: {
        requestId: string
        typeKey: string
        title: string
        data: unknown
        position?: { x: number; y: number } | null
      }) => void
    }
  }
}

test.describe('AI floating panels — User Story 1 (AI presents a visual response as a panel)', () => {
  test('opening a valid panel request renders it over the viewer while the viewer stays interactive (SC-008)', async ({
    page,
  }) => {
    await page.goto('/studio')

    await page.evaluate(() => {
      const store = (window as unknown as FloatingPanelDevtools).__askLucyFloatingPanelStore
      store.getState().openPanel({
        requestId: 'e2e-chart-1',
        typeKey: 'chart',
        title: 'Daily Sun Exposure',
        data: { chartKind: 'bar', series: [{ label: 'Exposure (hrs)', values: [4, 6, 8] }] },
      })
    })

    await expect(page.getByRole('region', { name: 'Daily Sun Exposure' })).toBeVisible()

    // The underlying viewer surface is still present and not obscured entirely — FR-003/SC-008.
    const viewer = page.getByTestId(/^viewer-(placeholder|fallback|map)$/)
    await expect(viewer).toBeVisible()
  })

  test('an unknown panel type produces a visible fallback, never nothing happening (FR-016, SC-007)', async ({
    page,
  }) => {
    await page.goto('/studio')

    await page.evaluate(() => {
      const store = (window as unknown as FloatingPanelDevtools).__askLucyFloatingPanelStore
      store.getState().openPanel({
        requestId: 'e2e-unknown-1',
        typeKey: 'does-not-exist',
        title: 'Mystery Panel',
        data: {},
      })
    })

    await expect(page.getByRole('region', { name: 'Mystery Panel' })).toBeVisible()
    await expect(page.getByText(/unsupported panel type/i)).toBeVisible()
  })

  test('malformed data for a known type produces a visible fallback, never a blank or crashed panel (FR-017)', async ({
    page,
  }) => {
    await page.goto('/studio')

    await page.evaluate(() => {
      const store = (window as unknown as FloatingPanelDevtools).__askLucyFloatingPanelStore
      store.getState().openPanel({
        requestId: 'e2e-invalid-1',
        typeKey: 'chart',
        title: 'Bad Data',
        data: { nonsense: true },
      })
    })

    await expect(page.getByRole('region', { name: 'Bad Data' })).toBeVisible()
    await expect(page.getByText(/couldn't be loaded/i)).toBeVisible()
  })

  test('multiple panels opened without an explicit position cascade instead of stacking exactly (FR-021)', async ({
    page,
  }) => {
    await page.goto('/studio')

    await page.evaluate(() => {
      const store = (window as unknown as FloatingPanelDevtools).__askLucyFloatingPanelStore
      store.getState().openPanel({
        requestId: 'e2e-cascade-1',
        typeKey: 'table',
        title: 'Panel One',
        data: { columns: ['A'], rows: [] },
      })
      store.getState().openPanel({
        requestId: 'e2e-cascade-2',
        typeKey: 'table',
        title: 'Panel Two',
        data: { columns: ['A'], rows: [] },
      })
    })

    const first = await page.getByRole('region', { name: 'Panel One' }).boundingBox()
    const second = await page.getByRole('region', { name: 'Panel Two' }).boundingBox()
    expect(first).not.toBeNull()
    expect(second).not.toBeNull()
    if (first && second) {
      expect(second.x).toBeGreaterThan(first.x)
      expect(second.y).toBeGreaterThan(first.y)
    }
  })
})
