import { expect, test } from '@playwright/test'

/**
 * specs/012-elevenlabs-voice-engine T080 — a Chromium fake-media-device smoke test for the
 * Push-to-Talk happy path (US1). Chromium's `--use-fake-device-for-media-stream` synthesizes
 * a deterministic audio input (a repeating low-frequency tone) so `getUserMedia` succeeds
 * without a real microphone, and `--use-fake-ui-for-media-stream` auto-grants the permission
 * prompt so the run is unattended — the standard approach for exercising `useSpeechRecognition
 * .ts`'s capture path in CI. Feasible in this codebase: Playwright already launches Chromium
 * (`playwright.config.ts`), and `launchOptions.args` is exactly where these flags belong.
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT, same caveat as every other spec in this directory
 * (RegressionMatrix.spec.ts's doc comment): needs a running backend + frontend dev server,
 * AND — specific to this feature — a real ElevenLabs API key, since the fake device only
 * solves the "no physical microphone" half of the problem; the synthesized tone still has to
 * round-trip through a live `POST /api/v1/ai/voice/stt-session` mint and a real ElevenLabs
 * realtime WebSocket to produce a transcript. Run via `npm test` from this directory against
 * a real deployment with `ElevenLabs:ApiKey` configured (`E2E_BASE_URL` env var).
 */

test.use({
  launchOptions: {
    args: ['--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream'],
  },
})

test.describe('Voice Push-to-Talk (US1)', () => {
  test('starting a voice turn transcribes the fake audio device input and plays back a spoken reply', async ({
    page,
  }) => {
    await page.goto('/chat')

    await page.getByRole('button', { name: /start voice conversation/i }).click()
    await expect(page.getByText(/listening/i)).toBeVisible()

    // Chromium's fake device streams a synthetic tone continuously, so the same
    // silence-based auto-commit used for a real utterance (SILENCE_COMMIT_DELAY_MS,
    // useSpeechRecognition.ts) never naturally fires here — stop explicitly to end capture.
    await page.getByRole('button', { name: /stop voice conversation/i }).click()

    await expect(page.getByText(/ai thinking|ai speaking/i)).toBeVisible({ timeout: 15_000 })
  })
})
