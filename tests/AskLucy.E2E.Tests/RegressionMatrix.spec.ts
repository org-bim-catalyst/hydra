import { expect, test } from '@playwright/test'

/**
 * End-to-end regression matrix (spec.md § quickstart.md §4, tasks.md T029).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: these scenarios require a running instance of both
 * the backend (with a real SQL Server + OpenAI key) and the frontend dev server, plus a
 * seeded test account with a known password and TOTP secret — none of which exist in
 * this sandbox. Run via `npm test` from this directory against a real deployment
 * (`E2E_BASE_URL` env var), per quickstart.md.
 */

const TEST_USER = {
  email: process.env.E2E_TEST_EMAIL ?? 'e2e-test-user@asklucy.io',
  password: process.env.E2E_TEST_PASSWORD ?? '',
}

test.describe('Legacy capability regression matrix', () => {
  test('login with existing 2FA-enrolled account succeeds without re-enrollment', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Email').fill(TEST_USER.email)
    await page.getByLabel('Password').fill(TEST_USER.password)
    await page.getByRole('button', { name: 'Sign in' }).click()

    await expect(page.getByText('Enter your authenticator app code.')).toBeVisible()
  })

  test('chat message streams a response', async ({ page }) => {
    await page.goto('/chat')
    await page.getByPlaceholder('Message Ask Lucy...').fill('Say hello in one word.')
    await page.keyboard.press('Enter')

    await expect(page.locator('text=Hello').first()).toBeVisible({ timeout: 15_000 })
  })

  test('translate action produces translated text', async ({ page }) => {
    await page.goto('/chat')
    await page.getByLabel('Language').click()
    await page.getByRole('option', { name: 'French' }).click()
    await page.getByLabel('Translate last response').click()
  })

  test('image generation opens a generated image', async ({ page }) => {
    await page.goto('/chat')
    page.once('dialog', (dialog) => dialog.accept('A red bicycle'))

    const [popup] = await Promise.all([
      page.waitForEvent('popup'),
      page.getByLabel('Generate image').click(),
    ])

    await expect(popup).toHaveURL(/.+/)
  })

  test('theme toggle switches between light and dark', async ({ page }) => {
    await page.goto('/chat')
    const before = await page.evaluate(() => document.body.style.backgroundColor)
    await page.getByLabel('Toggle theme').click()
    const after = await page.evaluate(() => document.body.style.backgroundColor)
    expect(after).not.toBe(before)
  })

  test('mobile viewport collapses the chat sidebar behind a menu button', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    await page.goto('/chat')

    await expect(page.getByLabel('Open chat list')).toBeVisible()
  })
})
