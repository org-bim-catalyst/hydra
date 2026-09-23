import { ApiError } from '../../../../api/httpClient'

/** The Problem Details `detail` of a failed request, or a generic message when there isn't one. */
export const errorMessage = (err: unknown) =>
  err instanceof ApiError ? err.detail ?? err.message : 'Something went wrong. Please try again.'
