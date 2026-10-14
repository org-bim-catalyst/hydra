import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ExpandableActionGroup, type ExpandableActionGroupAction } from './ExpandableActionGroup'

describe('ExpandableActionGroup', () => {
  it('renders actions as an icon-only row by default', () => {
    render(
      <ExpandableActionGroup
        actions={[{ id: 'a', label: 'Action A', icon: <span aria-hidden="true">A</span> }]}
      />,
    )
    expect(screen.getByRole('button', { name: 'Action A' })).toBeInTheDocument()
  })

  it('calls onSelect when a row action is clicked', async () => {
    const onSelect = vi.fn()
    const user = userEvent.setup()
    render(<ExpandableActionGroup actions={[{ id: 'a', label: 'Action A', onSelect }]} />)
    await user.click(screen.getByRole('button', { name: 'Action A' }))
    expect(onSelect).toHaveBeenCalledTimes(1)
  })

  it('renders actions as an icon+label list when layout is list', () => {
    render(
      <ExpandableActionGroup layout="list" actions={[{ id: 'a', label: 'Profile' }]} />,
    )
    expect(screen.getByRole('button', { name: 'Profile' })).toBeInTheDocument()
  })

  it('highlights a highlighted action distinctly', () => {
    render(
      <ExpandableActionGroup
        actions={[
          { id: 'a', label: 'Normal' },
          { id: 'b', label: 'Run', highlighted: true },
        ]}
      />,
    )
    expect(screen.getByRole('button', { name: 'Run' })).toBeInTheDocument()
  })

  describe('tool groups (specs/081 US6)', () => {
    // jsdom cannot run getByRole once a MUI menu is open (it resolves styles for every node), so the open menu is
    // read through querySelector and getByText instead.
    const menu = () => document.querySelector<HTMLElement>('[role="toolbar"]')!
    const tools = (onSelect = vi.fn()): ExpandableActionGroupAction[] => [
      { id: 'a', label: 'Tool A', icon: <span aria-hidden="true">A</span>, onSelect },
      { id: 'b', label: 'Tool B', icon: <span aria-hidden="true">B</span>, onSelect: vi.fn(), disabled: true, disabledReason: 'Not now' },
      { id: 'c', label: 'Tool C', icon: <span aria-hidden="true">C</span>, onSelect: vi.fn() },
    ]
    const group = (extra: Partial<ExpandableActionGroupAction> = {}): ExpandableActionGroupAction => ({
      id: 'g', label: 'Tool A', tools: tools(), activeToolId: 'a', onChooseTool: vi.fn(), ...extra,
    })
    const trigger = () => screen.getByRole('button', { name: 'Tool A' })


    it('renders one button showing the active tool, marked as a menu button', () => {
      render(<ExpandableActionGroup actions={[group()]} />)
      expect(trigger()).toHaveAttribute('aria-haspopup', 'true')
      expect(trigger()).toHaveAttribute('aria-expanded', 'false')
    })

    it('runs the shown tool on click when the group is not active', () => {
      const onSelect = vi.fn()
      render(<ExpandableActionGroup actions={[group({ tools: tools(onSelect) })]} />)
      fireEvent.click(trigger())
      expect(onSelect).toHaveBeenCalledTimes(1)
    })

    it('opens the menu on click when the group is active, and choosing calls onChooseTool', () => {
      const onChooseTool = vi.fn()
      render(<ExpandableActionGroup actions={[group({ highlighted: true, onChooseTool })]} />)
      fireEvent.click(trigger())
      fireEvent.click(menu().querySelector<HTMLElement>('[aria-label="Tool C"]')!)
      expect(onChooseTool).toHaveBeenCalledWith('c')
    })

    it('opens the menu on right-click and from the corner mark', () => {
      render(<ExpandableActionGroup actions={[group()]} />)
      fireEvent.contextMenu(trigger())
      expect(menu()).toBeTruthy()
      fireEvent.keyDown(menu(), { key: 'Escape' })
      fireEvent.click(screen.getByTestId('tool-group-corner'))
      expect(menu()).toBeTruthy()
    })

    it('opens the menu after a 400 ms press, and not on the click that follows', async () => {
      const onSelect = vi.fn()
      render(<ExpandableActionGroup actions={[group({ tools: tools(onSelect) })]} />)
      fireEvent.pointerDown(trigger())
      await act(async () => {
        await new Promise((resolve) => setTimeout(resolve, 450))
      })
      expect(document.querySelector('[role="toolbar"]')).not.toBeNull()
      const button = document.querySelector<HTMLElement>('[aria-haspopup="true"]')!
      fireEvent.pointerUp(button)
      fireEvent.click(button)
      expect(onSelect).not.toHaveBeenCalled()
    })

    it('shows the reason a disabled tool cannot be chosen', () => {
      render(<ExpandableActionGroup actions={[group()]} />)
      fireEvent.contextMenu(trigger())
      const item = menu().querySelector<HTMLButtonElement>('[aria-label="Tool B"]')!
      expect(item).toBeDisabled()
    })

    it('opens with the Down arrow, and choosing from the menu calls onChooseTool', () => {
      const onChooseTool = vi.fn()
      render(<ExpandableActionGroup actions={[group({ highlighted: true, onChooseTool })]} />)
      fireEvent.keyDown(trigger(), { key: 'ArrowDown' })
      expect(menu()).toBeTruthy()
      fireEvent.click(menu().querySelector<HTMLElement>('[aria-label="Tool C"]')!)
      expect(onChooseTool).toHaveBeenCalledWith('c')
    })

    it('a Down arrow moves focus to the first enabled tool, so the keyboard can reach the flyout', () => {
      render(<ExpandableActionGroup actions={[group({ highlighted: true })]} />)
      fireEvent.keyDown(trigger(), { key: 'ArrowDown' })
      expect(document.activeElement).toBe(menu().querySelector('[aria-label="Tool C"]'))
    })
  })
})
