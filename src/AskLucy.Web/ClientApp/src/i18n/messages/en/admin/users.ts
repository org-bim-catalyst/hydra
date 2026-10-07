// specs/067 Phase 13 (T212) — admin 'users' catalog (English): the users console, its row menu and the bulk dialogs.
export const enAdminUsers = {
  page: {
    title: 'User management',
    subtitle: {
      one: '{count} registered user',
      other: '{count} registered users',
    },
    searchLabel: 'Search by name or email',
    loadFailed: 'Could not load the users.',
    retry: 'Try again',
    generalError: 'Something went wrong. Please try again.',
  },
  selection: {
    count: '{count} selected',
    lockSelected: 'Lock selected',
    unlockSelected: 'Unlock selected',
    force2faReset: 'Force 2FA reset',
    delete: 'Delete',
  },
  table: {
    selectAll: 'Select all eligible users on this page',
    selectUser: 'Select {email}',
    empty: 'No users found.',
    columns: {
      email: 'Email',
      firstName: 'First name',
      lastName: 'Last name',
      role: 'Role',
      status: 'Status',
      emailConfirmed: 'Email confirmed',
      twoFactor: '2FA enabled',
      registered: 'Registered',
      actions: 'Actions',
    },
    status: {
      locked: 'Locked',
      active: 'Active',
    },
    confirmation: {
      confirmed: 'Confirmed',
      pending: 'Pending',
    },
    twoFactor: {
      enabled: 'Enabled',
      disabled: 'Disabled',
    },
  },
  pagination: {
    rowsPerPage: 'Rows per page:',
    displayedRows: '{from}–{to} of {count}',
    first: 'Go to first page',
    last: 'Go to last page',
    next: 'Go to next page',
    previous: 'Go to previous page',
  },
  bulk: {
    action: {
      lock: 'Lock',
      unlock: 'Unlock',
      forceReset2fa: 'Force 2FA reset',
      delete: 'Delete',
    },
    progress: {
      lock: 'Locking',
      unlock: 'Unlocking',
      forceReset2fa: 'Resetting 2FA for',
      delete: 'Deleting',
      fallback: 'Running “{action}” on',
    },
    confirmTitle: '{action} selected items?',
    confirmBody: {
      one: 'Do you want to {action} {count} item?',
      other: 'Do you want to {action} {count} items?',
    },
    running: '{verb} {done} of {total}…',
    completeTitle: '{action} complete',
    succeeded: {
      one: '{count} item succeeded.',
      other: '{count} items succeeded.',
    },
    skipped: '{count} skipped:',
    cancel: 'Cancel',
    done: 'Done',
    failed: 'Something went wrong. Please try again.',
  },
  scope: {
    selectPage: {
      one: 'Select the {count} item on this page only',
      other: 'Select the {count} items on this page only',
    },
    deselectPage: {
      one: 'Deselect the {count} item on this page only',
      other: 'Deselect the {count} items on this page only',
    },
    selectAll: {
      one: 'Select all {count} matching items',
      other: 'Select all {count} matching items',
    },
    deselectAll: {
      one: 'Deselect all {count} matching items',
      other: 'Deselect all {count} matching items',
    },
    resolving: 'Resolving total matching…',
    select: 'Select',
    deselect: 'Deselect',
    cancel: 'Cancel',
  },
  menu: {
    actionsFor: 'Actions for {email}',
    unlock: 'Unlock account',
    lock: 'Lock account',
    changeRole: 'Change role…',
    force2faReset: 'Force 2FA reset',
    sendPasswordReset: 'Send password reset link',
    resendConfirmation: 'Resend confirmation email',
    delete: 'Delete account',
    cancel: 'Cancel',
    confirm: 'Confirm',
    generalError: 'Something went wrong. Please try again.',
    passwordResetSent: 'Password reset link sent to {email}.',
    confirmationResent: 'Confirmation email resent to {email}.',
    confirmCopy: {
      lock: {
        title: 'Lock this account?',
        body: 'The user will no longer be able to sign in until unlocked.',
      },
      force2fa: {
        title: 'Force a 2FA reset?',
        body: "The user's existing authenticator enrollment will be cleared; they'll need to re-enroll.",
      },
      delete: {
        title: 'Delete this account?',
        body: 'The account will be deactivated and can no longer sign in. This cannot be undone from this screen.',
      },
    },
  },
} as const
