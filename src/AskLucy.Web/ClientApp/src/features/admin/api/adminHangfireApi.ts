import { apiFetch } from '../../../api/httpClient'

/**
 * Mints the short-lived `askLucyHangfireSession` cookie (specs/060-hangfire-dashboard-access)
 * that lets the next top-level navigation to `/hangfire` authenticate. The cookie itself is set
 * by the server response (`HttpOnly`) — this call has no return value to act on beyond success.
 */
export const postHangfireSession = () => apiFetch<void>('/admin/hangfire/session', { method: 'POST' })
