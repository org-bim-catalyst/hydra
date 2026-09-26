import { apiFetch } from '../../../api/httpClient'

/** The built-in role every account holds unless given another one — never deletable or renamable. */
export const DEFAULT_ROLE_NAME = 'User'

export function isDefaultRole(role: { name: string; isBuiltIn: boolean }): boolean {
  return role.isBuiltIn && role.name === DEFAULT_ROLE_NAME
}

export interface RoleSummary {
  id: string
  name: string
  description: string | null
  isBuiltIn: boolean
  permissionKeys: string[]
  userCount: number
  modifiedAtUtc: string | null
  concurrencyStamp: string
  /** The built-in User role: editable by addition only, never deleted or renamed. */
  isDefault: boolean
  /** Permissions that can't be taken off the role (the User role's baseline). */
  lockedPermissionKeys: string[]
}

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
}

export interface GetRolesParams {
  search?: string
  page?: number
  pageSize?: number
}

export function getRoles(params: GetRolesParams = {}) {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  query.set('page', String(params.page ?? 1))
  query.set('pageSize', String(params.pageSize ?? 20))

  return apiFetch<PagedResult<RoleSummary>>(`/admin/roles?${query.toString()}`)
}

export function getRole(roleId: string) {
  return apiFetch<RoleSummary>(`/admin/roles/${roleId}`)
}

export interface CreateRolePayload {
  name: string
  description?: string | null
  permissionKeys: string[]
}

export function createRole(payload: CreateRolePayload) {
  return apiFetch<RoleSummary>('/admin/roles', { method: 'POST', body: JSON.stringify(payload) })
}

export interface UpdateRolePayload {
  name: string
  description?: string | null
  permissionKeys: string[]
  concurrencyStamp: string
}

export function updateRole(roleId: string, payload: UpdateRolePayload) {
  return apiFetch<RoleSummary>(`/admin/roles/${roleId}`, { method: 'PUT', body: JSON.stringify(payload) })
}

export interface UpdateDefaultRolePayload {
  description?: string | null
  permissionKeys: string[]
  concurrencyStamp: string
}

/** The User role: description and added permissions only — its name and baseline are fixed. */
export function updateDefaultRole(payload: UpdateDefaultRolePayload) {
  return apiFetch<RoleSummary>('/admin/roles/default', { method: 'PUT', body: JSON.stringify(payload) })
}

export interface DuplicateRolePayload {
  name: string
  description?: string | null
}

/** Super User only — saves any role under a new name as a custom role with the same permissions. */
export function duplicateRole(roleId: string, payload: DuplicateRolePayload) {
  return apiFetch<RoleSummary>(`/admin/roles/${roleId}/duplicate`, { method: 'POST', body: JSON.stringify(payload) })
}

export interface AdministratorContentAccess {
  granted: boolean
}

/** Whether the built-in Administrator role holds View user content (specs/074 FR-016g). */
export function getAdministratorContentAccess() {
  return apiFetch<AdministratorContentAccess>('/admin/roles/administrator/content-access')
}

/** Super User only — the server refuses anyone else with a 403 whose detail names the reason. */
export function setAdministratorContentAccess(granted: boolean) {
  return apiFetch<AdministratorContentAccess>('/admin/roles/administrator/content-access', {
    method: 'PUT',
    body: JSON.stringify({ granted }),
  })
}

export interface DeleteRoleResult {
  /** How many holders were moved to the User role. */
  reassignedUserCount: number
}

export function deleteRole(roleId: string, concurrencyStamp: string) {
  return apiFetch<DeleteRoleResult>(
    `/admin/roles/${roleId}?concurrencyStamp=${encodeURIComponent(concurrencyStamp)}`,
    { method: 'DELETE' },
  )
}

export interface RoleAssignmentRole {
  id: string
  name: string
  isBuiltIn: boolean
}

export interface RoleAssignment {
  userId: string
  email: string
  firstName: string | null
  lastName: string | null
  isLockedOut: boolean
  role: RoleAssignmentRole | null
}

export interface GetRoleAssignmentsParams {
  search?: string
  roleId?: string
  assignableOnly?: boolean
  page?: number
  pageSize?: number
}

export function getRoleAssignments(params: GetRoleAssignmentsParams = {}) {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  if (params.roleId) query.set('roleId', params.roleId)
  query.set('assignableOnly', String(params.assignableOnly ?? false))
  query.set('page', String(params.page ?? 1))
  query.set('pageSize', String(params.pageSize ?? 20))

  return apiFetch<PagedResult<RoleAssignment>>(`/admin/role-assignments?${query.toString()}`)
}

export function assignRole(userId: string, roleId: string | null, expectedCurrentRoleId: string | null) {
  return apiFetch<void>(`/admin/role-assignments/${userId}`, {
    method: 'PUT',
    body: JSON.stringify({ roleId, expectedCurrentRoleId }),
  })
}

export interface BulkTargetRequest {
  ids: string[] | null
  allMatching: boolean
  search?: string | null
}

export interface BulkActionSkip {
  id: string
  reason: string
}

export function getRolesEligibleIds(search?: string) {
  const query = new URLSearchParams()
  if (search) query.set('search', search)
  return apiFetch<{ ids: string[] }>(`/admin/roles/actions/bulk-eligible-ids?${query.toString()}`)
}

export interface BulkDeleteRolesResult {
  succeededCount: number
  skipped: BulkActionSkip[]
  reassignedUserCounts: Record<string, number>
}

export const bulkDeleteRoles = (target: BulkTargetRequest) =>
  apiFetch<BulkDeleteRolesResult>('/admin/roles/actions/bulk-delete', { method: 'POST', body: JSON.stringify(target) })

export function getRoleAssignmentsEligibleIds(roleId: string, search?: string) {
  const query = new URLSearchParams()
  query.set('roleId', roleId)
  if (search) query.set('search', search)
  return apiFetch<{ ids: string[] }>(`/admin/role-assignments/actions/bulk-eligible-ids?${query.toString()}`)
}

export interface BulkActionResult {
  succeededCount: number
  skipped: BulkActionSkip[]
}

export const bulkAssignRole = (roleId: string, target: BulkTargetRequest) =>
  apiFetch<BulkActionResult>('/admin/role-assignments/actions/bulk-assign', {
    method: 'POST',
    body: JSON.stringify({ roleId, ...target }),
  })
