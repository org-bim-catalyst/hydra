import { expect, test } from '@playwright/test'

/**
 * User Story 2 — build a workflow visually: search the node palette, add nodes, connect them with
 * a typed-connection check, undo/redo, save as draft and reload, and confirm a disconnected node
 * blocks Publish until reconnected (specs/022-workflow-orchestration-engine quickstart.md
 * Scenario 2; spec.md FR-007/FR-008/FR-009/FR-016).
 *
 * Node placement uses `NodePalette`'s click-to-add affordance rather than simulated HTML5
 * drag-and-drop — it's the same accessible (keyboard-/screen-reader-reachable) path the palette
 * exposes specifically so dropping isn't the only way to add a node, and it's far more reliable
 * under Playwright than simulating a native `dataTransfer`-based drag. Connecting two nodes *is*
 * exercised as a real drag, between `@xyflow/react`'s own stable `.react-flow__handle-*` DOM
 * classes, since that interaction has no non-drag equivalent in this UI.
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on AgentCreateAndRun.spec.ts). Written to
 * the same selector/assertion conventions as the existing Agent/Workflow E2E suite so it runs
 * unmodified once a real environment is wired into CI.
 */

test.describe('Workflow Designer canvas', () => {
  test('adding nodes, connecting them, undo/redo, save-draft reload, and disconnected-node validation', async ({ page }) => {
    await page.goto('/workflows')
    await page.getByRole('button', { name: 'New Workflow' }).click()
    await page.getByLabel('Name').fill('Designer Canvas Test')
    await page.getByRole('button', { name: 'Create Workflow' }).click()
    await expect(page).toHaveURL(/\/workflows\/[0-9a-f-]+/)

    const canvas = page.getByRole('application', { name: 'Workflow canvas' })

    // Search the palette, then add three nodes via the accessible click-to-add path.
    await page.getByLabel('Search node palette').fill('start')
    await page.getByRole('button', { name: 'Add Start node' }).click()
    await expect(canvas.getByText('Start', { exact: true })).toBeVisible()

    await page.getByLabel('Search node palette').fill('transform')
    await page.getByRole('button', { name: 'Add Transform node' }).click()
    await expect(canvas.getByText('Transform', { exact: true })).toBeVisible()

    await page.getByLabel('Search node palette').fill('end')
    await page.getByRole('button', { name: 'Add End node' }).click()
    await expect(canvas.getByText('End', { exact: true })).toBeVisible()

    // Connect Start → Transform by dragging between their handles (FR-008 type-compatibility is
    // checked live by `isValidConnection` before the edge is accepted).
    const startHandle = canvas.locator('.react-flow__node', { hasText: 'Start' }).locator('.react-flow__handle-right')
    const transformInputHandle = canvas.locator('.react-flow__node', { hasText: 'Transform' }).locator('.react-flow__handle-left')
    await startHandle.dragTo(transformInputHandle)
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(1)

    // Undo removes the connection; redo restores it.
    await canvas.click()
    await page.keyboard.press('Control+z')
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(0)
    await page.keyboard.press('Control+Shift+z')
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(1)

    // Transform → End stays unconnected on purpose — validation should flag it and Publish stays blocked.
    await page.getByRole('button', { name: 'Validate' }).click()
    await expect(page.getByText(/disconnected/i)).toBeVisible()
    await expect(page.getByRole('button', { name: 'Publish' })).toBeDisabled()

    // Save as draft, then reload — the same layout/connections/configuration should reappear.
    await page.getByRole('button', { name: 'Save Draft' }).click()
    await expect(page.getByText('Unsaved changes')).toHaveCount(0)
    await page.reload()
    await expect(canvas.getByText('Start', { exact: true })).toBeVisible()
    await expect(canvas.getByText('Transform', { exact: true })).toBeVisible()
    await expect(canvas.getByText('End', { exact: true })).toBeVisible()
    await expect(canvas.locator('.react-flow__edge')).toHaveCount(1)

    // Reconnecting the last node clears the disconnected-node violation.
    const transformOutputHandle = canvas.locator('.react-flow__node', { hasText: 'Transform' }).locator('.react-flow__handle-right')
    const endInputHandle = canvas.locator('.react-flow__node', { hasText: 'End' }).locator('.react-flow__handle-left')
    await transformOutputHandle.dragTo(endInputHandle)

    await page.getByRole('button', { name: 'Validate' }).click()
    await expect(page.getByText('No violations — this draft is ready to publish.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Publish' })).toBeEnabled()
  })
})
