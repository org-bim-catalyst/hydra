import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { SelectAllScopeDialog } from './SelectAllScopeDialog'

describe('SelectAllScopeDialog', () => {
  it('shows the page count and a disabled "all matching" option while it is still resolving', () => {
    render(
      <SelectAllScopeDialog open onClose={vi.fn()} verb="Select" pageCount={20} onChoose={vi.fn()} />,
    )

    expect(screen.getByText('Select the 20 items on this page only')).toBeInTheDocument()
    expect(screen.getByText('Resolving total matching…')).toBeInTheDocument()
  })

  it('shows the all-matching total once resolved and confirms the page-only choice by default', () => {
    const onChoose = vi.fn()
    render(
      <SelectAllScopeDialog open onClose={vi.fn()} verb="Select" pageCount={20} totalCount={40} onChoose={onChoose} />,
    )

    expect(screen.getByText('Select all 40 matching items')).toBeInTheDocument()

    fireEvent.click(screen.getByText('Select'))

    expect(onChoose).toHaveBeenCalledWith('page')
  })

  it('confirms the "all matching" choice when that radio is picked', () => {
    const onChoose = vi.fn()
    render(
      <SelectAllScopeDialog open onClose={vi.fn()} verb="Select" pageCount={20} totalCount={40} onChoose={onChoose} />,
    )

    fireEvent.click(screen.getByText('Select all 40 matching items'))
    fireEvent.click(screen.getByText('Select'))

    expect(onChoose).toHaveBeenCalledWith('all')
  })

  it('uses the Deselect verb throughout when deselecting', () => {
    render(
      <SelectAllScopeDialog open onClose={vi.fn()} verb="Deselect" pageCount={5} totalCount={12} onChoose={vi.fn()} />,
    )

    expect(screen.getByText('Deselect the 5 items on this page only')).toBeInTheDocument()
    expect(screen.getByText('Deselect all 12 matching items')).toBeInTheDocument()
    expect(screen.getByText('Deselect')).toBeInTheDocument()
  })
})
