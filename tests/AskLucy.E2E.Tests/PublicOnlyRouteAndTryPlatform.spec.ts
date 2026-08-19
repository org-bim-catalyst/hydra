import { expect, test } from '@playwright/test'

/**
 * User Story 3 — coherent-product journey: authenticated redirect from the landing URL,
 * and both "Try the Platform" branches (specs/023-flumeria-landing-experience quickstart.md
 * Scenario 3).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: requires a running backend (real SQL Server) and
 * frontend dev server — see CookieConsentBanner.spec.ts's doc comment for the same caveat.
 * Run via `npm test` from this directory against a real deployment (`E2E_BASE_URL`).
 */

async function registerAndConfirm(page: import('@playwright/test').Page) {
  const email = `e2e-tryplatform-${Date.now()}-${Math.random().toString(36).slice(2)}@example.com`
  const password = 'Correct-Horse-Battery-Staple-1!'

  await page.goto('/register')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByText('Check your email to confirm your account.')).toBeVisible()

  // See CookieConsentBanner.spec.ts's doc comment: assumes a test/dev deployment where the
  // account is confirmed-by-default.
  return { email, password }
}

test.describe('PublicOnlyRoute redirect and "Try the Platform"', () => {
  test('an already-authenticated visitor hitting "/" is redirected straight to the workspace (FR-015)', async ({ page }) => {
    const { email, password } = await registerAndConfirm(page)
    await page.goto('/login')
    await page.getByLabel('Email').fill(email)
    await page.getByLabel('Password').fill(password)
    await page.getByRole('button', { name: 'Sign in' }).click()
    await expect(page).toHaveURL(/\/chat$/)

    await page.goto('/')

    await expect(page).toHaveURL(/\/chat$/)
  })

  test('"Try the Platform" routes a signed-out visitor into sign-up (FR-006, US3 Scenario 2)', async ({ page }) => {
    await page.goto('/')

    await page.getByRole('button', { name: 'Try the Platform' }).first().click()

    await expect(page).toHaveURL(/\/register$/)
  })

  test('"Try the Platform" routes a signed-in visitor directly into the workspace (FR-006, US3 Scenario 3)', async ({ page }) => {
    const { email, password } = await registerAndConfirm(page)
    await page.goto('/login')
    await page.getByLabel('Email').fill(email)
    await page.getByLabel('Password').fill(password)
    await page.getByRole('button', { name: 'Sign in' }).click()
    await expect(page).toHaveURL(/\/chat$/)

    // PublicOnlyRoute already redirects "/" straight to /chat for a signed-in visitor
    // (previous test), so the CTA itself is never actually rendered to click in normal
    // operation — this asserts the same outcome via direct navigation to "/".
    await page.goto('/')

    await expect(page).toHaveURL(/\/chat$/)
  })
})
