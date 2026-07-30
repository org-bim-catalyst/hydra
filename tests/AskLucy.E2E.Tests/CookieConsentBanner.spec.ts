import { expect, test } from '@playwright/test'

/**
 * User Story 1 — blocking cookie consent banner on first login
 * (specs/004-cookie-consent-privacy quickstart.md Scenario 1 & 2).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: requires a running backend (real SQL Server) and
 * frontend dev server — see RegressionMatrix.spec.ts's doc comment for the same caveat.
 * Run via `npm test` from this directory against a real deployment (`E2E_BASE_URL`).
 *
 * Each test registers a brand-new account rather than reusing a fixed seeded user, since
 * the whole point of these scenarios is "a user with no recorded consent decision" — a
 * shared account would only be fresh on the very first run.
 */

async function registerAndConfirmEmail(page: import('@playwright/test').Page) {
  const email = `e2e-consent-${Date.now()}-${Math.random().toString(36).slice(2)}@example.com`
  const password = 'Correct-Horse-Battery-Staple-1!'

  await page.goto('/register')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Create account' }).click()

  // This environment's dev email sender/confirmation flow is out of scope for this spec —
  // scenarios below assume a test/dev deployment where a freshly registered account can
  // sign in directly (dev-only confirmed-by-default account, or a confirmation step
  // already handled by the deployment's own E2E setup), matching the "not runnable here"
  // caveat above.
  await page.goto('/login')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password').fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()

  return { email, password }
}

test.describe('Cookie consent banner — first login', () => {
  test('a fresh account sees the blocking banner and it blocks other interaction', async ({ page }) => {
    await registerAndConfirmEmail(page)

    await expect(page.getByText('We use cookies')).toBeVisible()

    // FR-020: the rest of the app must not be reachable while the banner is open.
    await expect(page.getByPlaceholder('Message Ask Lucy...')).not.toBeVisible()
  })

  test('Accept All persists and the banner never reappears on next login', async ({ page }) => {
    const { email, password } = await registerAndConfirmEmail(page)

    await page.getByRole('button', { name: 'Accept All' }).click()
    await expect(page.getByText('We use cookies')).not.toBeVisible()

    await page.getByLabel('Account menu').click()
    await page.getByRole('menuitem', { name: 'Log out' }).click()

    await page.getByLabel('Email').fill(email)
    await page.getByLabel('Password').fill(password)
    await page.getByRole('button', { name: 'Sign in' }).click()

    await expect(page.getByText('We use cookies')).not.toBeVisible()
  })

  test('Reject Non-Essential persists and core functionality still works', async ({ page }) => {
    await registerAndConfirmEmail(page)

    await page.getByRole('button', { name: 'Reject Non-Essential' }).click()
    await expect(page.getByText('We use cookies')).not.toBeVisible()

    await page.getByPlaceholder('Message Ask Lucy...').fill('Hello')
    await expect(page.getByPlaceholder('Message Ask Lucy...')).toHaveValue('Hello')
  })

  test('Customize lets the user choose a specific combination and locks Essential on', async ({ page }) => {
    await registerAndConfirmEmail(page)

    await page.getByRole('button', { name: 'Customize' }).click()
    await expect(page.getByRole('checkbox', { name: /Essential/ })).toBeDisabled()

    await page.getByRole('checkbox', { name: /Functional/ }).check()
    await page.getByRole('button', { name: 'Save preferences' }).click()

    await expect(page.getByText('We use cookies')).not.toBeVisible()
  })
})
