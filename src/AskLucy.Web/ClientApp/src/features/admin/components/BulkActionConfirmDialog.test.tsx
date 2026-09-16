import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { BulkActionConfirmDialog } from './BulkActionConfirmDialog'

// Same reasoning as ModelSyncDialog.test.tsx: MUI's Paper-based Dialog surfaces render an
// inline `--Paper-shadow` custom property jsdom's CSS length parser cannot resolve — use
// fireEvent + text/DOM queries instead of getByRole for anything inside it.

describe('BulkActionConfirmDialog', () => {
  it('asks for confirmation with the already-resolved item count', () => {
    render(
      <BulkActionConfirmDialog open onClose={vi.fn()} actionLabel="Delete" itemCount={22} onConfirm={vi.fn()} />,
    )

    expect(screen.getByText('Do you want to delete 22 items?')).toBeInTheDocument()
  })

  it('shows live progress while the action runs, then the result summary', async () => {
    const onConfirm = vi.fn(async (onProgress: (done: number, total: number) => void) => {
      onProgress(1, 2)
      onProgress(2, 2)
      return { succeededCount: 1, skipped: [{ id: 'user-1', reason: 'already locked' }] }
    })

    render(
      <BulkActionConfirmDialog open onClose={vi.fn()} actionLabel="Lock" itemCount={2} onConfirm={onConfirm} />,
    )

    fireEvent.click(screen.getByText('Lock'))

    await waitFor(() => expect(screen.getByText('Locking 2 of 2…')).toBeInTheDocument())
    await waitFor(() => expect(screen.getByText('1 item succeeded.')).toBeInTheDocument())
    expect(screen.getByText('1 skipped:')).toBeInTheDocument()
    expect(screen.getByText('already locked')).toBeInTheDocument()
  })

  it('uses the supplied progressVerb instead of deriving one from actionLabel', async () => {
    let resolveConfirm: (() => void) | undefined
    const onConfirm = vi.fn(
      () =>
        new Promise<{ succeededCount: number; skipped: never[] }>((resolve) => {
          resolveConfirm = () => resolve({ succeededCount: 3, skipped: [] })
        }),
    )

    render(
      <BulkActionConfirmDialog
        open
        onClose={vi.fn()}
        actionLabel="Force 2FA reset"
        progressVerb="Resetting 2FA for"
        itemCount={3}
        onConfirm={onConfirm}
      />,
    )

    fireEvent.click(screen.getByText('Force 2FA reset'))

    expect(await screen.findByText(/Resetting 2FA for 0 of 3/)).toBeInTheDocument()
    resolveConfirm?.()
  })

  it('shows an error and returns to the confirm step when onConfirm rejects', async () => {
    const onConfirm = vi.fn().mockRejectedValue(new Error('boom'))

    render(
      <BulkActionConfirmDialog open onClose={vi.fn()} actionLabel="Delete" itemCount={5} onConfirm={onConfirm} />,
    )

    fireEvent.click(screen.getByText('Delete'))

    await waitFor(() => expect(screen.getByText('Something went wrong. Please try again.')).toBeInTheDocument())
    expect(screen.getByText('Do you want to delete 5 items?')).toBeInTheDocument()
  })
})
