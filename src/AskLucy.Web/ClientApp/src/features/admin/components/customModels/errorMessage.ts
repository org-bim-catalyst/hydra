import { ApiError } from '../../../../api/httpClient'
import type { Translate } from '../../../../i18n/useT'

/**
 * The Problem Details `detail` of a failed request (already localized by the server), or a generic message in the
 * caller's language when there isn't one.
 */
export const errorMessage = (err: unknown, t: Translate<'admin.aiProviders'>) =>
  err instanceof ApiError ? (err.detail ?? err.message) : t('shared.genericError')
