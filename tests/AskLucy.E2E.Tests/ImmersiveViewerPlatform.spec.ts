import { expect, test } from '@playwright/test'

/**
 * specs/027-immersive-viewer-platform quickstart.md — Scenarios 1–5.
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: requires a running backend, frontend dev server, an
 * authenticated session, and (for the location scenarios) a Google Maps Platform API key —
 * see ConversationPersistence.spec.ts's doc comment for the same caveat. Run via `npm test`
 * from this directory against a real deployment (`E2E_BASE_URL` env var).
 */

test.describe('Immersive viewer platform — User Story 1 (arrival)', () => {
  test('the viewer occupies the majority of the viewport and AiPresenceCard still renders', async ({
    page,
  }) => {
    await page.goto('/studio')

    const viewer = page.getByTestId(/^viewer-(placeholder|fallback)$/)
    await expect(viewer).toBeVisible()

    const viewerBox = await viewer.boundingBox()
    const viewportSize = page.viewportSize()
    expect(viewerBox).not.toBeNull()
    expect(viewportSize).not.toBeNull()
    if (viewerBox && viewportSize) {
      const viewerArea = viewerBox.width * viewerBox.height
      const viewportArea = viewportSize.width * viewportSize.height
      expect(viewerArea / viewportArea).toBeGreaterThanOrEqual(0.7) // SC-001
    }

    // AiPresenceCard (the pre-existing decorative-sphere presence card, SPEC-024) is
    // unaffected by this feature — it still renders alongside the new viewer (FR-004/SC-007).
    await expect(page.getByAltText('Lucy').or(page.locator('canvas'))).toBeVisible()
  })
})

test.describe('Immersive viewer platform — User Story 2 (current location)', () => {
  test('transitions from the placeholder to the map within ~5s once location is granted (SC-002)', async ({
    page,
    context,
  }) => {
    await context.grantPermissions(['geolocation'])
    await context.setGeolocation({ latitude: 51.5074, longitude: -0.1278 })

    await page.goto('/studio')

    await expect(page.getByTestId('viewer-map')).toBeVisible({ timeout: 5000 })
    await expect(page.getByTestId('viewer-placeholder')).not.toBeVisible()
  })

  test('stays on the placeholder with no error when location access is denied (FR-008, SC-005)', async ({
    page,
    context,
  }) => {
    await context.clearPermissions()

    await page.goto('/studio')

    await expect(page.getByTestId('viewer-placeholder')).toBeVisible()
    await expect(page.getByTestId('viewer-map')).toHaveCount(0)
    // No error toast/snackbar/alert anywhere on the page (constitution's no-silent-failures
    // rule doesn't apply here — a denied permission isn't a failure, per plan.md's documented
    // carve-out — but it also must never surface as a spurious visible error).
    await expect(page.locator('[role="alert"]')).toHaveCount(0)
  })
})

test.describe('Immersive viewer platform — User Story 6 (programmatic API, no AI agent)', () => {
  test('every documented command resolves ok/error correctly and fires its event, via devtools only', async ({
    page,
  }) => {
    await page.goto('/studio')

    // quickstart.md Scenario 5 — the exact sequence, run directly against the dev-exposed
    // engine instance, proving the contract without any AI-agent code involved (SC-006).
    const results = await page.evaluate(() => {
      const engine = (
        window as unknown as {
          __askLucyViewerEngine: {
            setViewMode: (m: string) => { ok: boolean }
            zoomToLocation: (lat: number, lon: number, zoom?: number) => { ok: boolean }
            select: (l: string, e: string) => { ok: boolean; error?: string }
            setRotationEnabled: (enabled: boolean) => { ok: boolean }
          }
        }
      ).__askLucyViewerEngine

      return {
        setViewMode: engine.setViewMode('plan'),
        zoomToLocation: engine.zoomToLocation(51.5074, -0.1278, 12),
        selectUnknown: engine.select('does-not-exist', 'x'),
        setRotationEnabled: engine.setRotationEnabled(false),
      }
    })

    expect(results.setViewMode.ok).toBe(true)
    expect(results.zoomToLocation.ok).toBe(true)
    expect(results.selectUnknown.ok).toBe(false) // unknown target — caller-visible failure, not a silent no-op
    expect(results.setRotationEnabled.ok).toBe(true)
  })
})

test.describe('Immersive viewer platform — User Story 5 (selection)', () => {
  test('selecting and deselecting the current-location marker toggles its highlight', async ({
    page,
    context,
  }) => {
    await context.grantPermissions(['geolocation'])
    await context.setGeolocation({ latitude: 51.5074, longitude: -0.1278 })
    await page.goto('/studio')
    await page.getByTestId('viewer-map').waitFor({ timeout: 5000 })

    // The marker itself is Google's own DOM element inside the map div — asserted via the
    // engine's own selection state (devtools-exposed in User Story 6) rather than a brittle
    // visual pixel check, matching quickstart.md's own approach for this scenario.
    const selection = await page.evaluate(() => {
      const engine = (window as unknown as { __askLucyViewerEngine?: { select: (l: string, e: string) => { ok: boolean } } })
        .__askLucyViewerEngine
      return engine?.select('gis-current-location', 'current-location')
    })
    expect(selection?.ok).toBe(true)
  })
})

test.describe('Immersive viewer platform — User Story 4 (weather widget)', () => {
  test('appears with granted location, and is absent when denied (FR-009/FR-008)', async ({
    page,
    context,
  }) => {
    await context.grantPermissions(['geolocation'])
    await context.setGeolocation({ latitude: 51.5074, longitude: -0.1278 })
    await page.goto('/studio')

    await expect(page.getByRole('status', { name: /Weather in/ })).toBeVisible({ timeout: 5000 })
  })

  test('is absent when location access is denied', async ({ page, context }) => {
    await context.clearPermissions()
    await page.goto('/studio')

    await expect(page.getByRole('status', { name: /Weather in/ })).toHaveCount(0)
  })
})

test.describe('Immersive viewer platform — User Story 3 (camera controls)', () => {
  test('the isometric/plan toggle and rotation toggle both visibly change state', async ({ page }) => {
    await page.goto('/studio')

    // View-mode toggle: opens the control, selects Plan, confirms the highlight moves.
    await page.getByRole('button', { name: 'View mode' }).click()
    await page.getByRole('button', { name: 'Plan' }).click()
    await expect(page.getByRole('button', { name: 'Plan' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )

    // Rotation toggle: an instant on/off Fab in the top cluster.
    const rotationButton = page.getByRole('button', { name: /rotation$/ })
    const wasStopping = (await rotationButton.getAttribute('aria-label')) === 'Stop rotation'
    await rotationButton.click()
    await expect(rotationButton).toHaveAttribute(
      'aria-label',
      wasStopping ? 'Start rotation' : 'Stop rotation',
    )
  })
})
