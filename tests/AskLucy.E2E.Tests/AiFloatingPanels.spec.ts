import { expect, test } from '@playwright/test'

/**
 * specs/028-ai-floating-panels quickstart.md — Scenarios 1 onward, updated by specs/049 to the
 * discriminated content/live request shape (contracts/panel-request.md). Content panels replace
 * what used to be the four built-in registered types (chart, table, parameters, summary) with
 * composed blocks; assertions and wording follow accordingly. The panel *behaviours* under test —
 * drag, resize, minimize/restore, close, focus/z-order, opacity, cascade, context association —
 * are unchanged (specs/049 FR-027) and this file's job is exactly to prove that.
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

interface PanelChromeOverride {
  titleBar?: boolean
  resizable?: boolean
  defaultSize?: { width: number; height: number }
}

type PanelRequest =
  | {
      kind: 'content'
      requestId: string
      title: string
      content: unknown
      chrome?: PanelChromeOverride | null
      position?: { x: number; y: number } | null
      contextAssociation?: { layerId?: string; elementId?: string } | null
    }
  | {
      kind: 'live'
      requestId: string
      title: string
      typeKey: string
      data: unknown
      chrome?: PanelChromeOverride | null
      position?: { x: number; y: number } | null
      contextAssociation?: { layerId?: string; elementId?: string } | null
    }

interface FloatingPanelDevtools {
  __askLucyFloatingPanelStore: {
    getState: () => {
      panels: { id: string; title: string; validationStatus: string }[]
      openPanel: (request: PanelRequest) => void
    }
  }
  __askLucyViewerEngine: {
    addLayer: (layer: { id: string; kind: string }) => unknown
    removeLayer: (layerId: string) => unknown
  }
}

/** A single-column table content document — the composed-block equivalent of the retired `table`
 * panel type, used throughout this file wherever the test only cares that *some* panel is open,
 * not what it shows. */
function simpleTableContent() {
  return { version: 1, blocks: [{ kind: 'table', columns: ['A'], rows: [] }] }
}

test.describe('AI floating panels — User Story 1 (Lucy presents composed content as a panel)', () => {
  test('opening a valid content request renders it over the viewer while the viewer stays interactive (SC-008)', async ({
    page,
  }) => {
    await page.goto('/studio')

    await page.evaluate(() => {
      const store = (window as unknown as FloatingPanelDevtools).__askLucyFloatingPanelStore
      store.getState().openPanel({
        kind: 'content',
        requestId: 'e2e-chart-1',
        title: 'Daily Sun Exposure',
        content: {
          version: 1,
          blocks: [{ kind: 'chart', chartKind: 'bar', series: [{ label: 'Exposure (hrs)', values: [4, 6, 8] }] }],
        },
      })
    })

    await expect(page.getByRole('region', { name: 'Daily Sun Exposure' })).toBeVisible()

    // The underlying viewer surface is still present and not obscured entirely — FR-003/SC-008.
    const viewer = page.getByTestId(/^viewer-(placeholder|fallback|map)$/)
    await expect(viewer).toBeVisible()
  })

  test('an unknown live panel kind produces a visible fallback, never nothing happening (spec FR-025)', async ({
    page,
  }) => {
    await page.goto('/studio')

    await page.evaluate(() => {
      const store = (window as unknown as FloatingPanelDevtools).__askLucyFloatingPanelStore
      store.getState().openPanel({
        kind: 'live',
        requestId: 'e2e-unknown-1',
        typeKey: 'does-not-exist',
        title: 'Mystery Panel',
        data: {},
      })
    })

    await expect(page.getByRole('region', { name: 'Mystery Panel' })).toBeVisible()
    await expect(page.getByText(/unsupported panel type/i)).toBeVisible()
  })

  test('a malformed block produces a visible per-block error while every sibling still renders (spec User Story 4)', async ({
    page,
  }) => {
    await page.goto('/studio')

    await page.evaluate(() => {
      const store = (window as unknown as FloatingPanelDevtools).__askLucyFloatingPanelStore
      store.getState().openPanel({
        kind: 'content',
        requestId: 'e2e-invalid-1',
        title: 'Bad Data',
        content: {
          version: 1,
          blocks: [
            { kind: 'heading', text: 'This renders' },
            { kind: 'table', columns: [] },
          ],
        },
      })
    })

    await expect(page.getByRole('region', { name: 'Bad Data' })).toBeVisible()
    await expect(page.getByText('This renders')).toBeVisible()
    await expect(page.getByText(/couldn't be displayed/i)).toBeVisible()
  })

  test('multiple panels opened without an explicit position cascade instead of stacking exactly (FR-021)', async ({
    page,
  }) => {
    await page.goto('/studio')

    await page.evaluate(() => {
      const store = (window as unknown as FloatingPanelDevtools).__askLucyFloatingPanelStore
      store.getState().openPanel({ kind: 'content', requestId: 'e2e-cascade-1', title: 'Panel One', content: simpleTableContent() })
      store.getState().openPanel({ kind: 'content', requestId: 'e2e-cascade-2', title: 'Panel Two', content: simpleTableContent() })
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
        kind: 'content',
        requestId: 'e2e-drag-1',
        title: 'Draggable Panel',
        content: simpleTableContent(),
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

  test('a panel declaring itself fixed-size shows no resize handles (specs/049 US3, FR-020)', async ({ page }) => {
    await page.goto('/studio')
    await page.evaluate(() => {
      const store = (window as unknown as FloatingPanelDevtools).__askLucyFloatingPanelStore
      store.getState().openPanel({
        kind: 'content',
        requestId: 'e2e-fixed-1',
        title: 'Fixed Panel',
        content: { version: 1, blocks: [{ kind: 'keyValue', items: [{ label: 'X', value: 1 }] }] },
        chrome: { resizable: false },
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
        kind: 'content',
        requestId: 'e2e-minimize-1',
        title: 'Minimize Me',
        content: simpleTableContent(),
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
      store.getState().openPanel({ kind: 'content', requestId: 'e2e-close-1', title: 'Close Me', content: simpleTableContent() })
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
        kind: 'content',
        requestId: 'e2e-focus-back',
        title: 'Back Panel',
        content: simpleTableContent(),
        position: { x: 60, y: 60 },
      })
      store.getState().openPanel({
        kind: 'content',
        requestId: 'e2e-focus-front',
        title: 'Front Panel',
        content: simpleTableContent(),
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

test.describe('AI floating panels — User Story 3 (opacity preference)', () => {
  test('changing the Settings opacity slider applies immediately to open panels (SC-005)', async ({ page }) => {
    await page.goto('/studio')
    await page.evaluate(() => {
      const store = (window as unknown as FloatingPanelDevtools).__askLucyFloatingPanelStore
      store.getState().openPanel({ kind: 'content', requestId: 'e2e-opacity-1', title: 'Opacity Panel', content: simpleTableContent() })
    })
    const panel = page.getByRole('region', { name: 'Opacity Panel' })
    const opacityBefore = await panel.evaluate((el) => getComputedStyle(el).backgroundColor)

    await page.goto('/settings', { state: { tab: 8 } })
    const slider = page.getByRole('slider', { name: /panel opacity/i })
    await slider.focus()
    for (let i = 0; i < 25; i += 1) {
      await page.keyboard.press('ArrowLeft')
    }

    await page.goto('/studio')
    const opacityAfter = await page
      .getByRole('region', { name: 'Opacity Panel' })
      .evaluate((el) => getComputedStyle(el).backgroundColor)

    // Layout itself isn't persisted (spec Assumption), so this is a fresh panel, but it picks up
    // the just-saved preference immediately rather than the previous default — no reload needed.
    expect(opacityAfter).not.toBe(opacityBefore)
  })

  test('the opacity preference persists across a reload (FR-012)', async ({ page }) => {
    await page.goto('/settings', { state: { tab: 8 } })
    const slider = page.getByRole('slider', { name: /panel opacity/i })
    await slider.focus()
    for (let i = 0; i < 25; i += 1) {
      await page.keyboard.press('ArrowLeft')
    }
    await expect
      .poll(() => page.evaluate(() => localStorage.getItem('ask-lucy-panel-preferences')))
      .not.toBeNull()
    const savedBeforeReload = await page.evaluate(() => localStorage.getItem('ask-lucy-panel-preferences'))

    await page.reload()

    const savedAfterReload = await page.evaluate(() => localStorage.getItem('ask-lucy-panel-preferences'))
    expect(savedAfterReload).toBe(savedBeforeReload)
    expect(JSON.parse(savedAfterReload ?? '{}').state.opacityPercent).toBeLessThan(85)
  })
})

test.describe('AI floating panels — User Story 4 (panel reacts to and informs the viewer)', () => {
  test('the Locate action on a context-associated panel drives the viewer (FR-014, US4-AS1)', async ({ page }) => {
    await page.goto('/studio')
    await page.evaluate(() => {
      const engine = (window as unknown as FloatingPanelDevtools).__askLucyViewerEngine
      engine.addLayer({ id: 'e2e-ctx-layer', kind: 'model' })
      const store = (window as unknown as FloatingPanelDevtools).__askLucyFloatingPanelStore
      store.getState().openPanel({
        kind: 'content',
        requestId: 'e2e-ctx-1',
        title: 'Context Panel',
        content: { version: 1, blocks: [{ kind: 'heading', text: 'Site Notes' }, { kind: 'text', text: 'Demo' }] },
        contextAssociation: { layerId: 'e2e-ctx-layer', elementId: 'e2e-ctx-element' },
      })
    })

    // Clicking Locate must not throw even though 'e2e-ctx-element' was never registered as
    // selectable — viewerEngine.select() fails gracefully (contracts/viewer-engine-api.md), it
    // doesn't crash the panel or the viewer (constitution §2.VIII no-silent-crash).
    await page.getByRole('button', { name: /locate in viewer/i }).click()
    await expect(page.getByRole('region', { name: 'Context Panel' })).toBeVisible()
  })

  test("removing a panel's associated viewer layer marks its association invalid (US4-AS2, Edge Cases)", async ({
    page,
  }) => {
    await page.goto('/studio')
    await page.evaluate(() => {
      const engine = (window as unknown as FloatingPanelDevtools).__askLucyViewerEngine
      engine.addLayer({ id: 'e2e-ctx-layer-2', kind: 'model' })
      const store = (window as unknown as FloatingPanelDevtools).__askLucyFloatingPanelStore
      store.getState().openPanel({
        kind: 'content',
        requestId: 'e2e-ctx-2',
        title: 'Stale Panel',
        content: { version: 1, blocks: [{ kind: 'heading', text: 'Site Notes' }, { kind: 'text', text: 'Demo' }] },
        contextAssociation: { layerId: 'e2e-ctx-layer-2' },
      })
    })
    await expect(page.getByRole('img', { name: /association is (stale|no longer valid)/i })).toHaveCount(0)

    await page.evaluate(() => {
      const engine = (window as unknown as FloatingPanelDevtools).__askLucyViewerEngine
      engine.removeLayer('e2e-ctx-layer-2')
    })

    await expect(page.getByRole('img', { name: /association is no longer valid/i })).toBeVisible()
  })
})
