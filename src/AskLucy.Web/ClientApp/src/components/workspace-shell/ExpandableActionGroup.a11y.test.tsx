import { cleanup, fireEvent, render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { afterEach, describe, expect, it } from 'vitest'
import { ExpandableActionGroup } from './ExpandableActionGroup'

expect.extend(toHaveNoViolations)

afterEach(cleanup)

// specs/081 US6 (T041): the tool-group menu, open, has no detectable WCAG violations.
describe('ExpandableActionGroup accessibility (tool group)', () => {
  it('has no automatically detectable violations with the menu open', async () => {
    const { baseElement } = render(
      <ExpandableActionGroup
        actions={[
          {
            id: 'g',
            label: 'Tool A',
            activeToolId: 'a',
            onChooseTool: () => {},
            tools: [
              { id: 'a', label: 'Tool A', icon: <span aria-hidden="true">A</span> },
              { id: 'b', label: 'Tool B', icon: <span aria-hidden="true">B</span> },
            ],
          },
        ]}
      />,
    )
    fireEvent.contextMenu(document.querySelector('[aria-haspopup="true"]')!)
    // The open menu is a portal outside any landmark, so the axe region rule (page landmarks) does not apply to it.
    expect(await axe(baseElement, { rules: { region: { enabled: false } } })).toHaveNoViolations()
  })
})
