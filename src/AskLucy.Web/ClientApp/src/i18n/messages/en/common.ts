import type { MessageTree } from '../../types'

/** Shared buttons, states and error text used by every localized surface. */
export const enCommon = {
  actions: {
    retry: 'Retry',
    reload: 'Reload',
    delete: 'Delete',
    close: 'Close',
    save: 'Save',
  },
  states: {
    loading: 'Loading…',
    loadingPleaseWait: 'Loading… please wait',
    noLongerAvailable: 'No longer available',
  },
  errors: {
    generic: 'Something went wrong. Please try again.',
  },
} satisfies MessageTree
