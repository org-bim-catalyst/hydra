import { ApiError } from '../../../api/httpClient'

/**
 * The Problem Details `detail` of a failed request, or a generic message when there isn't one. The server has
 * already localized `detail` for localized surfaces (R15), so it is shown as returned; pass a translated
 * `fallback` (`common.errors.generic`) for the generic case.
 */
export const errorMessage = (err: unknown, fallback = 'Something went wrong. Please try again.') =>
  err instanceof ApiError ? (err.detail ?? err.message) : fallback
