import { expect, test } from '@playwright/test'

/**
 * User Story 2 — landing page → sign-in → workspace (specs/023-flumeria-landing-experience
 * quickstart.md Scenario 2).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: requires a running backend (real SQL Server) and
 * frontend dev server — see CookieConsentBanner.spec.ts's doc comment for the same caveat.
 * Run via `npm test` from this directory against a real deployment (`E2E_BASE_URL`).
 */

async function registerAndConfirm(page: import('@playwright/test').Page) {
  const email = `e2e-signin-${Date.now()}-${Math.random().toString(36).slice(2)}@example.com`
  const password = 'Correct-Horse-Battery-Staple-1!'

  await page.goto('/register')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByText('Check your email to confirm your account.')).toBeVisible()

  // See CookieConsentBanner.spec.ts's doc comment: this environment's dev email
  // sender/confirmation flow is out of scope for this spec — assumes a test/dev
  // deployment where the account is confirmed-by-default, matching that existing caveat.
  return { email, password }
}

test.describe('Landing page → sign-in → workspace', () => {
  test('a returning user reaches the workspace via the landing page Sign In CTA', async ({ page }) => {
    const { email, password } = await registerAndConfirm(page)

    await page.goto('/')
    await page.getByRole('button', { name: 'Sign In' }).first().click()
    await expect(page).toHaveURL(/\/login$/)

    await page.getByLabel('Email').fill(email)
    await page.getByLabel('Password').fill(password)
    await page.getByRole('button', { name: 'Sign in' }).click()

    await expect(page).toHaveURL(/\/chat$/)
  })

  test('the workspace header shows the Flumeria brand-transition element alongside Ask Lucy (FR-011)', async ({ page }) => {
    const { email, password } = await registerAndConfirm(page)

    await page.goto('/login')
    await page.getByLabel('Email').fill(email)
    await page.getByLabel('Password').fill(password)
    await page.getByRole('button', { name: 'Sign in' }).click()
    await expect(page).toHaveURL(/\/chat$/)

    await expect(page.getByText('Flumeria')).toBeVisible()
    await expect(page.getByText('Ask Lucy')).toBeVisible()
  })

  test('an invalid password shows a visible error and preserves the entered email (FR-017)', async ({ page }) => {
    await page.goto('/login')

    await page.getByLabel('Email').fill('someone@example.com')
    await page.getByLabel('Password').fill('definitely-wrong')
    await page.getByRole('button', { name: 'Sign in' }).click()

    await expect(page.getByText('Invalid email or password.')).toBeVisible()
    await expect(page.getByLabel('Email')).toHaveValue('someone@example.com')
  })
})
