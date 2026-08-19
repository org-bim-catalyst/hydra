import { expect, test } from '@playwright/test'

/**
 * User Story 1 — landing page discovery → sign-up (specs/023-flumeria-landing-experience
 * quickstart.md Scenario 1).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: requires a running backend (real SQL Server) and
 * frontend dev server — see CookieConsentBanner.spec.ts's doc comment for the same caveat.
 * Run via `npm test` from this directory against a real deployment (`E2E_BASE_URL`).
 *
 * Registration issues no session and triggers no redirect (spec.md FR-008, Clarifications
 * — the platform's existing email-confirmation requirement is unchanged), so this journey
 * ends at the branded confirmation-pending state, not the workspace. Reaching the workspace
 * from a fresh account is covered separately by LandingToSignin.spec.ts, after confirming
 * by email and signing in.
 */

test.describe('Landing page → sign-up', () => {
  test('a signed-out visitor sees the landing page, not a login form, at the root URL', async ({ page }) => {
    await page.goto('/')

    await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
    await expect(page.getByLabel('Email')).not.toBeVisible()
  })

  test('Create Account / Sign Up leads to a brand-consistent sign-up page', async ({ page }) => {
    await page.goto('/')

    await page.getByRole('button', { name: 'Create Account / Sign Up' }).first().click()

    await expect(page).toHaveURL(/\/register$/)
    await expect(page.getByRole('heading', { name: 'Create your account' })).toBeVisible()
  })

  test('completing sign-up shows the confirmation-pending state, not a workspace redirect', async ({ page }) => {
    const email = `e2e-landing-${Date.now()}-${Math.random().toString(36).slice(2)}@example.com`

    await page.goto('/')
    await page.getByRole('button', { name: 'Create Account / Sign Up' }).first().click()
    await page.getByLabel('Email').fill(email)
    await page.getByLabel('Password', { exact: true }).fill('Correct-Horse-Battery-Staple-1!')
    await page.getByRole('button', { name: 'Create account' }).click()

    await expect(page.getByText('Check your email to confirm your account.')).toBeVisible()
    await expect(page).toHaveURL(/\/register$/)
  })

  test('Sign In leads to a brand-consistent sign-in page', async ({ page }) => {
    await page.goto('/')

    await page.getByRole('button', { name: 'Sign In' }).first().click()

    await expect(page).toHaveURL(/\/login$/)
    await expect(page.getByRole('heading', { name: 'Welcome back' })).toBeVisible()
  })

  test('the landing page is fully responsive at a small mobile viewport', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 800 })
    await page.goto('/')

    await expect(page.getByRole('button', { name: 'Create Account / Sign Up' }).first()).toBeVisible()
    const bodyScrollWidth = await page.evaluate(() => document.body.scrollWidth)
    expect(bodyScrollWidth).toBeLessThanOrEqual(360)
  })
})
