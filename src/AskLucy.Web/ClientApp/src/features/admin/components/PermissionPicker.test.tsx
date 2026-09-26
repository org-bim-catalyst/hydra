import { fireEvent, render, screen, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useIsSuperUser } from '../../../hooks/useIsSuperUser'
import { ADMIN_PERMISSIONS } from '../adminPermissions'
import { PermissionPicker, SUPER_USER_ONLY_HINT } from './PermissionPicker'

vi.mock('../../../hooks/useIsSuperUser', () => ({ useIsSuperUser: vi.fn(() => false) }))

const CONTENT_VIEW_LABEL = 'View user content in failure investigations'

function operationalFailuresArea(): HTMLElement {
  return screen.getByText('Operational failures').parentElement!
}

function renderPicker(selectedKeys: string[]) {
  const onChange = vi.fn()
  render(<PermissionPicker selectedKeys={selectedKeys} onChange={onChange} />)
  return onChange
}

// specs/074 T081 (FR-016h, FR-016i) — View user content is visible to everyone but only a Super
// User can tick or untick it; everyone else's saves carry the stored value through unchanged.
describe('PermissionPicker — Super-User-controlled keys', () => {
  beforeEach(() => {
    vi.mocked(useIsSuperUser).mockReturnValue(false)
  })

  it('disables the checkbox for a non-Super-User but keeps it checked', () => {
    renderPicker([ADMIN_PERMISSIONS.operationalFailuresView, ADMIN_PERMISSIONS.operationalFailuresContentView])

    const checkbox = screen.getByLabelText(CONTENT_VIEW_LABEL)
    expect(checkbox).toBeDisabled()
    expect(checkbox).toBeChecked()
  })

  it('explains why with the "Only a Super User can grant this" tooltip', async () => {
    renderPicker([])

    fireEvent.mouseOver(screen.getByText(CONTENT_VIEW_LABEL))

    expect(await screen.findByText(SUPER_USER_ONLY_HINT)).toBeInTheDocument()
  })

  it("echoes the stored value when a non-Super-User changes another permission", () => {
    const onChange = renderPicker([ADMIN_PERMISSIONS.operationalFailuresView, ADMIN_PERMISSIONS.operationalFailuresContentView])

    fireEvent.click(within(operationalFailuresArea()).getByLabelText('Manage'))

    expect(onChange).toHaveBeenCalledWith(expect.arrayContaining([ADMIN_PERMISSIONS.operationalFailuresContentView]))
  })

  it('lets a Super User grant it', () => {
    vi.mocked(useIsSuperUser).mockReturnValue(true)
    const onChange = renderPicker([ADMIN_PERMISSIONS.operationalFailuresView])

    const checkbox = screen.getByLabelText(CONTENT_VIEW_LABEL)
    expect(checkbox).toBeEnabled()
    fireEvent.click(checkbox)

    expect(onChange).toHaveBeenCalledWith(
      expect.arrayContaining([ADMIN_PERMISSIONS.operationalFailuresView, ADMIN_PERMISSIONS.operationalFailuresContentView]),
    )
  })

  it("removing it leaves the area's Manage in place — only the area's own View takes Manage with it", () => {
    vi.mocked(useIsSuperUser).mockReturnValue(true)
    const onChange = renderPicker([
      ADMIN_PERMISSIONS.operationalFailuresView,
      ADMIN_PERMISSIONS.operationalFailuresManage,
      ADMIN_PERMISSIONS.operationalFailuresContentView,
    ])

    fireEvent.click(screen.getByLabelText(CONTENT_VIEW_LABEL))

    expect(onChange).toHaveBeenCalledWith(
      expect.arrayContaining([ADMIN_PERMISSIONS.operationalFailuresView, ADMIN_PERMISSIONS.operationalFailuresManage]),
    )
    expect(onChange.mock.calls[0][0]).not.toContain(ADMIN_PERMISSIONS.operationalFailuresContentView)
  })
})

// The User role's basic permissions are shown ticked but can't be removed, and keys the role may
// never carry are left out of the catalog altogether.
describe('PermissionPicker — basic and hidden keys', () => {
  function usersArea(): HTMLElement {
    return screen.getByText('Users').parentElement!
  }

  it('shows a basic permission ticked and locked even when the selection omits it', () => {
    render(<PermissionPicker selectedKeys={[]} onChange={vi.fn()} lockedKeys={[ADMIN_PERMISSIONS.usersView]} />)

    const checkbox = within(usersArea()).getByLabelText('View')
    expect(checkbox).toBeChecked()
    expect(checkbox).toBeDisabled()
    expect(within(usersArea()).getByLabelText('Manage')).toBeEnabled()
  })

  it('leaves hidden keys out of the catalog', () => {
    render(
      <PermissionPicker
        selectedKeys={[]}
        onChange={vi.fn()}
        hiddenKeys={new Set([ADMIN_PERMISSIONS.operationalFailuresContentView])}
      />,
    )

    expect(screen.queryByText(CONTENT_VIEW_LABEL)).not.toBeInTheDocument()
    expect(within(operationalFailuresArea()).getByLabelText('View')).toBeInTheDocument()
  })
})
