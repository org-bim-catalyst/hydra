import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { FloatingToolbar } from './FloatingToolbar'

describe('FloatingToolbar', () => {
  it('renders its children', () => {
    render(
      <FloatingToolbar anchor="bottom-end">
        <button type="button">One</button>
        <button type="button">Two</button>
      </FloatingToolbar>,
    )
    expect(screen.getByRole('button', { name: 'One' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Two' })).toBeInTheDocument()
  })

  it.each([
    ['top-start', { top: '0px', left: '0px' }],
    ['top-end', { top: '0px', right: '0px' }],
    ['bottom-start', { bottom: '0px', left: '0px' }],
    ['bottom-end', { bottom: '0px', right: '0px' }],
  ] as const)('positions itself at the %s anchor', (anchor, expected) => {
    const { container } = render(
      <FloatingToolbar anchor={anchor}>
        <button type="button">One</button>
      </FloatingToolbar>,
    )
    const root = container.firstElementChild as HTMLElement
    const style = window.getComputedStyle(root)
    for (const [prop, value] of Object.entries(expected)) {
      expect(style[prop as keyof CSSStyleDeclaration]).toBe(value)
    }
  })

  it.each([
    ['top-start', 'wrap'],
    ['top-end', 'wrap'],
    ['bottom-start', 'wrap-reverse'],
    ['bottom-end', 'wrap-reverse'],
  ] as const)('wraps extra rows away from a %s anchor (flex-wrap: %s) so they never grow past the screen edge (FR-020)', (anchor, expectedWrap) => {
    const { container } = render(
      <FloatingToolbar anchor={anchor}>
        <button type="button">One</button>
      </FloatingToolbar>,
    )
    const root = container.firstElementChild as HTMLElement
    expect(window.getComputedStyle(root).flexWrap).toBe(expectedWrap)
  })
})
