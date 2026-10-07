import type { MessagesOf } from '../../../types'
import type { enAdminDashboard } from '../../en/admin/dashboard'

export const arAdminDashboard = {
  title: 'لوحة تحكم المسؤول',
  subtitle: 'نظرة سريعة على سلامة المنصة واستخدامها',
  stats: {
    totalUsers: 'إجمالي المستخدمين',
    twoFactorAdoption: 'نسبة تفعيل 2FA',
    activeUsers: 'المستخدمون النشطون',
    lockedOut: 'الحسابات المقفلة',
  },
  loadFailed: 'تعذّر تحميل لوحة التحكم.',
  retry: 'حاول مرة أخرى',
  noUsersYet: 'لا يوجد مستخدمون مسجلون بعد.',
  trend: {
    title: 'المستخدمون الجدد — آخر 30 يومًا',
    empty: 'لا توجد تسجيلات جديدة في هذه الفترة.',
    ariaLabel: 'عدد المستخدمين المسجلين حديثًا يوميًا خلال آخر 30 يومًا، بإجمالي {total}',
    barTitle: '{date}: {value}',
  },
  roles: {
    title: 'توزيع الأدوار',
    ariaLabel: 'توزيع الأدوار: {summary}',
    ariaItem: '{role} {count}',
    listSeparator: '، ',
    item: '{role}: {count}',
  },
  split: {
    ariaLabel: '{title}: {primaryLabel} {primaryCount}، {secondaryLabel} {secondaryCount}',
    item: '{label}: {count}',
    activeVsLocked: {
      title: 'النشطون مقابل المقفلون',
      primary: 'نشط',
      secondary: 'مقفل',
    },
    confirmedVsPending: {
      title: 'البريد المؤكَّد مقابل المعلَّق',
      primary: 'مؤكَّد',
      secondary: 'معلَّق',
    },
  },
} satisfies MessagesOf<typeof enAdminDashboard>
