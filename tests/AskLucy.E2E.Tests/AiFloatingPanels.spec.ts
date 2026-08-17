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

test.describe('AI floating panels — User Story 2 (user manages panel layout)', () => {
  test('dragging a panel by its title bar moves it and it stays where released (FR-004, SC-002)', async ({
    page,
  }) => {
    await page.goto('/studio')
    await page.evaluate(() => {
      const store = (window as unknown as FloatingPanelDevtools).__askLucyFloatingPanelStore
      store.getState().openPanel({
        requestId: 'e2e-drag-1',
        typeKey: 'table',
        title: 'Draggable Panel',
        data: { columns: ['A'], rows: [] },
        position: { x: 60, y: 60 },
      })
    })

    const panel = page.getByRole('region', { name: 'Draggable Panel' })
    const before = await panel.boundingBox()
    expect(before).not.toBeNull()
    if (!before) return

    const handle = panel.locator('.floating-panel-drag-handle')
    const handleBox = await handle.boundingBox()
    expect(handleBox).not.toBeNull()
    if (!handleBox) return

    await page.mouse.move(handleBox.x + handleBox.width / 2, handleBox.y + handleBox.height / 2)
    await page.mouse.down()
    await page.mouse.move(handleBox.x + 150, handleBox.y + 120, { steps: 10 })
    await page.mouse.up()

    const after = await panel.boundingBox()
    expect(after).not.toBeNull()
    if (after) {
      expect(after.x).toBeGreaterThan(before.x + 50)
      expect(after.y).toBeGreaterThan(before.y + 50)
    }
  })

  test('a fixed-size panel type shows no resize handles (FR-005, US2-AS3)', async ({ page }) => {
    await page.goto('/studio')
    await page.evaluate(() => {
      const store = (window as unknown as FloatingPanelDevtools).__askLucyFloatingPanelStore
      store.getState().openPanel({
        requestId: 'e2e-fixed-1',
        typeKey: 'parameters',
        title: 'Fixed Panel',
        data: { fields: [{ key: 'x', label: 'X', type: 'number', value: 1 }] },
      })
    })

    const panel = page.getByRole('region', { name: 'Fixed Panel' })
    await expect(panel).toBeVisible()
    // react-rnd renders resize handles as elements with a `resizable-handle-*` class when
    // `enableResizing` is anything other than `false` — none should exist for a fixed panel.
    await expect(page.locator('.react-resizable-handle')).toHaveCount(0)
  })

  test('minimize collapses to a compact bar and restore returns to the exact prior size/position (FR-006)', async ({
    page,
  }) => {
    await page.goto('/studio')
    await page.evaluate(() => {
      const store = (window as unknown as FloatingPanelDevtools).__askLucyFloatingPanelStore
      store.getState().openPanel({
        requestId: 'e2e-minimize-1',
        typeKey: 'table',
        title: 'Minimize Me',
        data: { columns: ['A'], rows: [] },
        position: { x: 80, y: 80 },
      })
    })

    const panel = page.getByRole('region', { name: 'Minimize Me' })
    const before = await panel.boundingBox()

    await page.getByRole('button', { name: /minimize panel/i }).click()
    const minimized = await panel.boundingBox()
    expect(minimized?.height).toBeLessThan(before?.height ?? Infinity)

    await page.getByRole('button', { name: /restore panel/i }).click()
    const restored = await panel.boundingBox()
    expect(restored).toEqual(before)
  })

  test('closing a panel removes it entirely (FR-007)', async ({ page }) => {
    await page.goto('/studio')
    await page.evaluate(() => {
      const store = (window as unknown as FloatingPanelDevtools).__askLucyFloatingPanelStore
      store.getState().openPanel({
        requestId: 'e2e-close-1',
        typeKey: 'table',
        title: 'Close Me',
        data: { columns: ['A'], rows: [] },
      })
    })

    await expect(page.getByRole('region', { name: 'Close Me' })).toBeVisible()
    await page.getByRole('button', { name: /close panel/i }).click()
    await expect(page.getByRole('region', { name: 'Close Me' })).toHaveCount(0)
  })

  test('clicking a background panel brings it to the front (FR-009)', async ({ page }) => {
    await page.goto('/studio')
    await page.evaluate(() => {
      const store = (window as unknown as FloatingPanelDevtools).__askLucyFloatingPanelStore
      store.getState().openPanel({
        requestId: 'e2e-focus-back',
        typeKey: 'table',
        title: 'Back Panel',
        data: { columns: ['A'], rows: [] },
        position: { x: 60, y: 60 },
      })
      store.getState().openPanel({
        requestId: 'e2e-focus-front',
        typeKey: 'table',
        title: 'Front Panel',
        data: { columns: ['A'], rows: [] },
        position: { x: 70, y: 70 },
      })
    })

    await page.getByRole('region', { name: 'Back Panel' }).click({ position: { x: 5, y: 5 } })

    // `react-rnd` sets `style.zIndex` on its own root node, one level above the `role="region"`
    // Box this component renders inside it.
    const backZ = await page
      .getByRole('region', { name: 'Back Panel' })
      .evaluate((el) => (el.parentElement as HTMLElement).style.zIndex)
    const frontZ = await page
      .getByRole('region', { name: 'Front Panel' })
      .evaluate((el) => (el.parentElement as HTMLElement).style.zIndex)
    expect(Number(backZ)).toBeGreaterThan(Number(frontZ))
  })
})
