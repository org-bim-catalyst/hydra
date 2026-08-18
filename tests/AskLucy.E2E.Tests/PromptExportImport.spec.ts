import { expect, test } from '@playwright/test'
import { mkdtempSync, writeFileSync } from 'fs'
import { tmpdir } from 'os'
import path from 'path'

/**
 * User Story 7 — export and import prompts (specs/019-prompt-library-workspace quickstart.md
 * Scenario 7).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — same constraint documented on PromptLifecycle.spec.ts.
 */

test.describe('Prompt export/import', () => {
  test('single export → import round-trip recreates the prompt', async ({ page }) => {
    await page.goto('/prompts')

    const downloadPromise = page.waitForEvent('download')
    await page.getByRole('button', { name: 'Export' }).click()
    await page.getByTestId('export-prompt-list').getByRole('button').first().click()
    await page.getByRole('button', { name: /Export \(1\)/ }).click()
    const download = await downloadPromise
    const filePath = path.join(mkdtempSync(path.join(tmpdir(), 'ask-lucy-e2e-')), 'single-prompt-export.json')
    await download.saveAs(filePath)

    await page.getByRole('button', { name: 'Import' }).click()
    const [fileChooser] = await Promise.all([
      page.waitForEvent('filechooser'),
      page.getByRole('button', { name: 'Choose file…' }).click(),
    ])
    await fileChooser.setFiles(filePath)
    await page.getByRole('button', { name: 'Import' }).last().click()

    await expect(page.getByRole('dialog')).toBeHidden()
    await expect(page.locator('[data-testid="prompt-card"]')).toHaveCount(2) // original + imported copy (auto-suffixed name)
  })

  test('a corrupted bundle entry rejects the whole import, nothing is created', async ({ page }) => {
    // A schema-valid-but-content-invalid entry (blank name, second in the bundle) — proves the
    // whole file is rejected, not just that one entry (FR-071, research.md Decision 13).
    const corruptedBundle = {
      schemaVersion: 1,
      prompts: [
        {
          name: 'Valid Prompt',
          description: null,
          promptType: 'Chat',
          systemInstructions: null,
          developerInstructions: null,
          userInstructions: 'Say hello.',
          contextText: null,
          examplesText: null,
          outputInstructions: null,
          constraints: null,
          requiredCapabilities: {
            requiresStreaming: false, requiresVision: false, requiresFunctionCalling: false, requiresJsonMode: false,
            requiresReasoning: false, requiresEmbeddings: false, requiresImageInput: false, requiresImageOutput: false, requiresAudio: false,
          },
          preferredModelKey: null,
          variables: [],
          tags: [],
        },
        {
          name: '   ',
          description: null,
          promptType: 'Chat',
          systemInstructions: null,
          developerInstructions: null,
          userInstructions: 'This entry is missing a name.',
          contextText: null,
          examplesText: null,
          outputInstructions: null,
          constraints: null,
          requiredCapabilities: {
            requiresStreaming: false, requiresVision: false, requiresFunctionCalling: false, requiresJsonMode: false,
            requiresReasoning: false, requiresEmbeddings: false, requiresImageInput: false, requiresImageOutput: false, requiresAudio: false,
          },
          preferredModelKey: null,
          variables: [],
          tags: [],
        },
      ],
    }
    const filePath = path.join(mkdtempSync(path.join(tmpdir(), 'ask-lucy-e2e-')), 'corrupted-bundle.json')
    writeFileSync(filePath, JSON.stringify(corruptedBundle), 'utf-8')

    await page.goto('/prompts')

    await page.getByRole('button', { name: 'Import' }).click()
    const [fileChooser] = await Promise.all([
      page.waitForEvent('filechooser'),
      page.getByRole('button', { name: 'Choose file…' }).click(),
    ])
    await fileChooser.setFiles(filePath)
    await page.getByRole('button', { name: 'Import' }).last().click()

    await expect(page.getByTestId('import-validation-errors')).toBeVisible()
    await expect(page.getByText(/nothing was created/i)).toBeVisible()
    await expect(page.getByText('Valid Prompt')).toHaveCount(0)
  })
})
