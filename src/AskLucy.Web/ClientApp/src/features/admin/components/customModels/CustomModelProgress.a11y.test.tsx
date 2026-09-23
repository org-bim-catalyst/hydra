import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it } from 'vitest'
import type { CustomModelSummary } from '../../api/adminCustomModelsApi'
import { customModel } from './customModelFixtures'
import { CustomModelProgress } from './CustomModelProgress'

expect.extend(toHaveNoViolations)

function renderProgress(model: CustomModelSummary) {
  const queryClient = new QueryClient()
  return render(
    <QueryClientProvider client={queryClient}>
      <CustomModelProgress model={model} canManage phase="Downloading" />
    </QueryClientProvider>,
  )
}

describe('CustomModelProgress accessibility', () => {
  it('has no automatically detectable a11y violations mid-transfer, and every bar is named and valued (constitution §10)', async () => {
    const { container } = renderProgress(
      customModel({
        deploymentState: 'Transferring',
        canCancel: true,
        totalBytes: 4096,
        transferredBytes: 1024,
        totalFileCount: 2,
        completedFileCount: 0,
        currentFilePath: 'model.onnx',
        currentFileBytes: 1024,
        currentFileTotalBytes: 2048,
      }),
    )

    const bars = screen.getAllByRole('progressbar')
    expect(bars).toHaveLength(2)
    for (const bar of bars) {
      expect(bar).toHaveAccessibleName()
      expect(bar).toHaveAttribute('aria-valuenow')
    }
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations while listing (constitution §10)', async () => {
    const { container } = renderProgress(
      customModel({ deploymentState: 'Listing', canCancel: true, totalBytes: null, totalFileCount: null }),
    )

    expect(screen.getByRole('progressbar')).toHaveAccessibleName()
    expect(await axe(container)).toHaveNoViolations()
  })
})
