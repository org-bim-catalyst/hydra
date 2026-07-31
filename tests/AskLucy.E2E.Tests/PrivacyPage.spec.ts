import { expect, test } from '@playwright/test'

/**
 * User Story 3 — public Privacy Page and policy-version re-consent
 * (specs/004-cookie-consent-privacy quickstart.md Scenario 4 & 5).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: requires a running backend (real SQL Server) and
 * frontend dev server — see RegressionMatrix.spec.ts's doc comment for the same caveat.
 * Run via `npm test` from this directory against a real deployment (`E2E_BASE_URL`).
 */

async function registerAndLogin(page: import('@playwright/test').Page) {
  const email = `e2e-privacy-${Date.now()}-${Math.random().toString(36).slice(2)}@example.com`
  const password = 'Correct-Horse-Battery-Staple-1!'

  await page.goto('/register')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Create account' }).click()

  await page.goto('/login')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password').fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()

  return { email, password }
}

test.describe('Privacy Page', () => {
  test('loads pre-login with full content and the live policy version', async ({ page }) => {
    await page.goto('/privacy')

    await expect(page).not.toHaveURL(/\/login/)
    await expect(page.getByText('Cookie categories')).toBeVisible()
    await expect(page.getByText(/Essential/)).toBeVisible()
    await expect(page.getByText(/Functional/)).toBeVisible()
    await expect(page.getByText(/Analytics/)).toBeVisible()
    await expect(page.getByText(/Marketing/)).toBeVisible()
    await expect(page.getByText('Third-party services')).toBeVisible()
    await expect(page.getByText('Data retention')).toBeVisible()
    await expect(page.getByText(/Policy version/)).toBeVisible()
  })

  test('is reachable in one click from the banner, the footer, and Settings', async ({ page, context }) => {
    await registerAndLogin(page)

    // From the banner.
    const [bannerPopup] = await Promise.all([
      context.waitForEvent('page'),
      page.getByRole('link', { name: 'Privacy Policy' }).click(),
    ])
    await expect(bannerPopup.getByText('Cookie categories')).toBeVisible()
    await bannerPopup.close()

    // Accept to clear the banner, then check the account-menu (global nav) link.
    await page.getByRole('button', { name: 'Accept All' }).click()
    await page.getByLabel('Account menu').click()
    await page.getByRole('menuitem', { name: 'Privacy Policy' }).click()
    await expect(page.getByText('Cookie categories')).toBeVisible()

    // From Settings > Cookies.
    await page.goto('/settings')
    await page.getByRole('tab', { name: 'Cookies' }).click()
    const [settingsPopup] = await Promise.all([
      context.waitForEvent('page'),
      page.getByRole('link', { name: 'Privacy Policy' }).click(),
    ])
    await expect(settingsPopup.getByText('Cookie categories')).toBeVisible()
  })

  test('a policy-version bump re-triggers the banner for a previously-consented user', async ({ page }) => {
    // Requires the deployment's CookiePolicy:CurrentVersion to be bumped between the two
    // logins below (simulated externally per quickstart.md Scenario 5 — not automatable
    // from within a single Playwright run without an admin/config-reload endpoint).
    const { email, password } = await registerAndLogin(page)
    await page.getByRole('button', { name: 'Accept All' }).click()
    await expect(page.getByText('We use cookies')).not.toBeVisible()

    await page.getByLabel('Account menu').click()
    await page.getByRole('menuitem', { name: 'Log out' }).click()

    // --- simulate a policy version bump here (deployment-level config change) ---

    await page.getByLabel('Email').fill(email)
    await page.getByLabel('Password').fill(password)
    await page.getByRole('button', { name: 'Sign in' }).click()

    await expect(page.getByText('We use cookies')).toBeVisible()
  })
})
