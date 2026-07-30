import { expect, test } from '@playwright/test'

/**
 * User Story 2 — manage cookie preferences from Settings
 * (specs/004-cookie-consent-privacy quickstart.md Scenario 3).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: requires a running backend (real SQL Server) and
 * frontend dev server — see RegressionMatrix.spec.ts's doc comment for the same caveat.
 * Run via `npm test` from this directory against a real deployment (`E2E_BASE_URL`).
 */

async function registerLoginAndAcceptAll(page: import('@playwright/test').Page) {
  const email = `e2e-settings-${Date.now()}-${Math.random().toString(36).slice(2)}@example.com`
  const password = 'Correct-Horse-Battery-Staple-1!'

  await page.goto('/register')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Create account' }).click()

  await page.goto('/login')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password').fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()

  // Clear the first-login banner so Settings > Cookies reflects an existing decision.
  await page.getByRole('button', { name: 'Accept All' }).click()
  await expect(page.getByText('We use cookies')).not.toBeVisible()
}

test.describe('Cookie preferences — Settings', () => {
  test('displays current preferences and last-updated timestamp', async ({ page }) => {
    await registerLoginAndAcceptAll(page)

    await page.goto('/settings')
    await page.getByRole('tab', { name: 'Cookies' }).click()

    await expect(page.getByText('Cookie preferences')).toBeVisible()
    await expect(page.getByText(/Last updated:/)).toBeVisible()
    await expect(page.getByRole('checkbox', { name: /Analytics/ })).toBeChecked()
  })

  test('toggling and saving updates immediately and refreshes the timestamp', async ({ page }) => {
    await registerLoginAndAcceptAll(page)

    await page.goto('/settings')
    await page.getByRole('tab', { name: 'Cookies' }).click()

    const before = await page.getByText(/Last updated:/).textContent()

    await page.getByRole('checkbox', { name: /Analytics/ }).uncheck()
    await page.getByRole('button', { name: 'Save preferences' }).click()

    await expect(page.getByText('Preferences saved.')).toBeVisible()
    await expect(page.getByRole('checkbox', { name: /Analytics/ })).not.toBeChecked()

    const after = await page.getByText(/Last updated:/).textContent()
    expect(after).not.toBe(before)
  })

  test('a simulated save failure shows a visible error and leaves prior preferences in effect', async ({ page }) => {
    await registerLoginAndAcceptAll(page)

    await page.goto('/settings')
    await page.getByRole('tab', { name: 'Cookies' }).click()

    // Simulate a network failure on the save request only.
    await page.route('**/api/v1/users/me/cookie-consent', (route) =>
      route.request().method() === 'PUT' ? route.abort() : route.continue(),
    )

    await page.getByRole('checkbox', { name: /Marketing/ }).uncheck()
    await page.getByRole('button', { name: 'Save preferences' }).click()

    await expect(page.getByText("Couldn't save your cookie preferences. Please try again.")).toBeVisible()
  })
})
