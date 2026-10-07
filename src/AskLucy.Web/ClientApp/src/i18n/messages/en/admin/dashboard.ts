// specs/067 Phase 13 (T211) — admin 'dashboard' catalog (English).
export const enAdminDashboard = {
  title: 'Admin Dashboard',
  subtitle: 'Platform health and usage at a glance',
  stats: {
    totalUsers: 'Total users',
    twoFactorAdoption: '2FA adoption',
    activeUsers: 'Active users',
    lockedOut: 'Locked out',
  },
  loadFailed: 'Could not load the dashboard.',
  retry: 'Try again',
  noUsersYet: 'No registered users yet.',
  trend: {
    title: 'New users — last 30 days',
    empty: 'No new registrations in this period.',
    ariaLabel: 'New user registrations per day over the last 30 days, {total} total',
    barTitle: '{date}: {value}',
  },
  roles: {
    title: 'Role distribution',
    ariaLabel: 'Role distribution: {summary}',
    ariaItem: '{role} {count}',
    listSeparator: ', ',
    item: '{role}: {count}',
  },
  split: {
    ariaLabel: '{title}: {primaryLabel} {primaryCount}, {secondaryLabel} {secondaryCount}',
    item: '{label}: {count}',
    activeVsLocked: {
      title: 'Active vs. locked out',
      primary: 'Active',
      secondary: 'Locked out',
    },
    confirmedVsPending: {
      title: 'Email confirmed vs. pending',
      primary: 'Confirmed',
      secondary: 'Pending',
    },
  },
} as const
