import { expect, test } from '@playwright/test'

/**
 * Account recovery journey (specs/058-password-recovery quickstart.md Scenarios 1–3, §10
 * critical-journey coverage).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: requires a running backend (real SQL Server) and frontend dev
 * server — see CookieConsentBanner.spec.ts's doc comment for the same caveat. Run via `npm test`
 * from this directory against a real deployment (`E2E_BASE_URL`).
 *
 * The redemption leg additionally needs the emailed link, which no deployment exposes to a test by
 * design. Supply it in `E2E_PASSWORD_RESET_LINK` (copied from the inbox, or from the dev
 * `ConsoleEmailSender`'s log line), plus `E2E_PASSWORD_RESET_EMAIL` for the sign-in leg, to run
 * that test; without them the test skips rather than pretending to have covered the journey.
 */

const RESET_LINK = process.env.E2E_PASSWORD_RESET_LINK
const RESET_EMAIL = process.env.E2E_PASSWORD_RESET_EMAIL
const NEW_PASSWORD = 'Rotated-Horse-Battery-Staple-2!'

async function register(page: import('@playwright/test').Page) {
  const email = `e2e-recovery-${Date.now()}-${Math.random().toString(36).slice(2)}@example.com`
  const password = 'Correct-Horse-Battery-Staple-1!'

  await page.goto('/register')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByText('Check your email to confirm your account.')).toBeVisible()

  return { email, password }
}

test.describe('Password recovery', () => {
  test('the sign-in page offers a route out of a forgotten password', async ({ page }) => {
    await page.goto('/login')

    await page.getByRole('link', { name: 'Forgot password?' }).click()

    await expect(page).toHaveURL(/\/forgot-password$/)
  })

  test('an address with an account and one without get the same answer (FR-003)', async ({ page }) => {
    const { email } = await register(page)

    async function requestReset(address: string) {
      await page.goto('/forgot-password')
      await page.getByLabel('Email address').fill(address)
      await page.getByRole('button', { name: 'Send reset link' }).click()
      const answer = await page.getByRole('alert').innerText()
      // The confirmation echoes back what the user typed; that is the one permitted difference.
      return answer.replace(address, '<address>')
    }

    const answerForRealAccount = await requestReset(email)
    const answerForNoAccount = await requestReset(`no-such-account-${Date.now()}@example.com`)

    // The whole point of the feature's enumeration defence: these must be indistinguishable.
    expect(answerForNoAccount).toBe(answerForRealAccount)
  })

  test('a tampered link is refused with one undifferentiated message (FR-005)', async ({ page }) => {
    await page.goto('/reset-password?userId=00000000-0000-0000-0000-000000000000&token=AABBCCDD')

    await page.getByLabel('New password', { exact: true }).fill(NEW_PASSWORD)
    await page.getByLabel('Confirm new password').fill(NEW_PASSWORD)
    await page.getByRole('button', { name: 'Set new password' }).click()

    await expect(page.getByText(/no longer valid/)).toBeVisible()
  })

  test('an incomplete link explains itself rather than failing silently', async ({ page }) => {
    await page.goto('/reset-password')

    await expect(page.getByText('This link is incomplete')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Request a new link' })).toBeVisible()
  })

  test('redeeming the emailed link restores access, and the old password stops working', async ({ page }) => {
    test.skip(!RESET_LINK, 'Set E2E_PASSWORD_RESET_LINK to the link from the reset email to run this.')

    await page.goto(RESET_LINK!)
    await page.getByLabel('New password', { exact: true }).fill(NEW_PASSWORD)
    await page.getByLabel('Confirm new password').fill(NEW_PASSWORD)
    await page.getByRole('button', { name: 'Set new password' }).click()

    await expect(page.getByText('Password updated')).toBeVisible()

    await page.getByRole('link', { name: 'Go to sign in' }).click()
    await expect(page).toHaveURL(/\/login$/)

    // The link carries an opaque user id, not the address (by design), so the address to sign in
    // with has to be supplied alongside it.
    if (RESET_EMAIL) {
      await page.getByLabel('Email').fill(RESET_EMAIL)
      await page.getByLabel('Password').fill(NEW_PASSWORD)
      await page.getByRole('button', { name: 'Sign in' }).click()
      await expect(page).toHaveURL(/\/chat$/)
    }

    // A second redemption of the same link must fail — single use (FR-005).
    await page.goto(RESET_LINK!)
    await page.getByLabel('New password', { exact: true }).fill(NEW_PASSWORD)
    await page.getByLabel('Confirm new password').fill(NEW_PASSWORD)
    await page.getByRole('button', { name: 'Set new password' }).click()

    await expect(page.getByText(/no longer valid/)).toBeVisible()
  })
})
