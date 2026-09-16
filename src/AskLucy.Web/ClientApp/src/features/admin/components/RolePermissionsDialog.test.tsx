import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { RoleSummary } from '../api/adminRolesApi'
import { RolePermissionsDialog } from './RolePermissionsDialog'

const customRole: RoleSummary = {
  id: 'role-1',
  name: 'Project Reviewer',
  description: 'Custom role',
  isBuiltIn: false,
  permissionKeys: ['admin.users.view', 'admin.ai-providers.view'],
  userCount: 3,
  modifiedAtUtc: null,
  concurrencyStamp: 'stamp-1',
}

describe('RolePermissionsDialog', () => {
  it('groups granted permissions by area, using catalog display names', () => {
    render(<RolePermissionsDialog open onClose={vi.fn()} role={customRole} />)

    expect(screen.getByText('Permissions for Project Reviewer')).toBeInTheDocument()
    expect(screen.getByText('Users')).toBeInTheDocument()
    expect(screen.getByText('View users')).toBeInTheDocument()
    expect(screen.getByText('AI providers')).toBeInTheDocument()
    expect(screen.getByText('View AI providers')).toBeInTheDocument()
    expect(screen.queryByText('Manage users')).not.toBeInTheDocument()
  })

  it('shows a message when the role has no permissions granted', () => {
    render(<RolePermissionsDialog open onClose={vi.fn()} role={{ ...customRole, permissionKeys: [] }} />)

    expect(screen.getByText('This role has no permissions granted.')).toBeInTheDocument()
  })
})
