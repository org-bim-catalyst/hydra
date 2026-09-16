import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { BulkActionConfirmDialog } from './BulkActionConfirmDialog'

// Same reasoning as ModelSyncDialog.test.tsx: MUI's Paper-based Dialog surfaces render an
// inline `--Paper-shadow` custom property jsdom's CSS length parser cannot resolve — use
// fireEvent + text/DOM queries instead of getByRole for anything inside it.

describe('BulkActionConfirmDialog', () => {
  it('shows the page-selected count and disables "all matching" until resolved', () => {
    render(
      <BulkActionConfirmDialog
        open
        onClose={vi.fn()}
        actionLabel="Lock"
        pageSelectedCount={20}
        onConfirm={vi.fn()}
      />,
    )

    expect(screen.getByText('20 selected on this page')).toBeInTheDocument()
    expect(screen.getByText('Resolving total matching…')).toBeInTheDocument()
  })

  it('shows the all-matching count once resolved and lets the admin pick it', () => {
    render(
      <BulkActionConfirmDialog
        open
        onClose={vi.fn()}
        actionLabel="Lock"
        pageSelectedCount={20}
        allMatchingCount={40}
        onConfirm={vi.fn()}
      />,
    )

    expect(screen.getByText('All 40 matching items')).toBeInTheDocument()
  })

  it('runs onConfirm with the selected scope and renders the result summary', async () => {
    const onConfirm = vi.fn().mockResolvedValue({
      succeededCount: 19,
      skipped: [{ id: 'user-1', reason: 'already locked' }],
    })

    render(
      <BulkActionConfirmDialog
        open
        onClose={vi.fn()}
        actionLabel="Lock"
        pageSelectedCount={20}
        allMatchingCount={40}
        onConfirm={onConfirm}
      />,
    )

    fireEvent.click(screen.getByText('Lock'))

    await waitFor(() => expect(onConfirm).toHaveBeenCalledWith('page'))
    await waitFor(() => expect(screen.getByText('19 items succeeded.')).toBeInTheDocument())
    expect(screen.getByText('1 skipped:')).toBeInTheDocument()
    expect(screen.getByText('already locked')).toBeInTheDocument()
  })

  it('confirms against the "all matching" scope when that radio is chosen', async () => {
    const onConfirm = vi.fn().mockResolvedValue({ succeededCount: 40, skipped: [] })

    render(
      <BulkActionConfirmDialog
        open
        onClose={vi.fn()}
        actionLabel="Delete"
        pageSelectedCount={20}
        allMatchingCount={40}
        onConfirm={onConfirm}
      />,
    )

    fireEvent.click(screen.getByText('All 40 matching items'))
    fireEvent.click(screen.getByText('Delete'))

    await waitFor(() => expect(onConfirm).toHaveBeenCalledWith('all'))
  })

  it('shows an error message when onConfirm rejects', async () => {
    const onConfirm = vi.fn().mockRejectedValue(new Error('boom'))

    render(
      <BulkActionConfirmDialog
        open
        onClose={vi.fn()}
        actionLabel="Lock"
        pageSelectedCount={20}
        onConfirm={onConfirm}
      />,
    )

    fireEvent.click(screen.getByText('Lock'))

    await waitFor(() => expect(screen.getByText('Something went wrong. Please try again.')).toBeInTheDocument())
  })
})
