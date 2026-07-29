import { apiFetch } from '../../../api/httpClient'

export interface DailyUserCount {
  date: string
  newUsers: number
}

export interface RoleCount {
  roleName: string
  userCount: number
}

export interface DashboardSummary {
  totalUsers: number
  newUsersLast30Days: DailyUserCount[]
  activeUsers: number
  lockedOutUsers: number
  emailConfirmedUsers: number
  emailPendingUsers: number
  twoFactorEnabledUsers: number
  roleDistribution: RoleCount[]
}

export const getDashboardSummary = () => apiFetch<DashboardSummary>('/admin/dashboard/summary')

export type UserRole = 'Administrator' | 'Super User' | 'Regular'

export interface UserAdmin {
  id: string
  email: string
  firstName: string | null
  lastName: string | null
  emailConfirmed: boolean
  twoFactorEnabled: boolean
  lockoutEnabled: boolean
  isLockedOut: boolean
  role: UserRole
  createdAtUtc: string
}

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
}

export type UserSortBy = 'email' | 'createdAtUtc'

export interface GetUsersParams {
  search?: string
  sortBy?: UserSortBy
  sortDescending?: boolean
  page?: number
  pageSize?: number
}

export function getUsers(params: GetUsersParams = {}) {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  query.set('sortBy', params.sortBy ?? 'email')
  query.set('sortDescending', String(params.sortDescending ?? false))
  query.set('page', String(params.page ?? 1))
  query.set('pageSize', String(params.pageSize ?? 20))

  return apiFetch<PagedResult<UserAdmin>>(`/users?${query.toString()}`)
}

export const lockUser = (userId: string) => apiFetch<void>(`/users/${userId}/actions/lock`, { method: 'POST' })

export const unlockUser = (userId: string) => apiFetch<void>(`/users/${userId}/actions/unlock`, { method: 'POST' })

export const changeUserRole = (userId: string, role: UserRole) =>
  apiFetch<void>(`/users/${userId}/role`, { method: 'PATCH', body: JSON.stringify({ role }) })

export const forceReset2fa = (userId: string) =>
  apiFetch<void>(`/users/${userId}/actions/force-2fa-reset`, { method: 'POST' })

export const deleteUser = (userId: string) => apiFetch<void>(`/users/${userId}`, { method: 'DELETE' })
