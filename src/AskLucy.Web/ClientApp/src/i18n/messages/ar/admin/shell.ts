import type { MessagesOf } from '../../../types'
import type { enAdminShell } from '../../en/admin/shell'

export const arAdminShell = {
  sidebar: {
    title: 'الإدارة',
    navLabel: 'أقسام الإدارة',
    expand: 'توسيع الشريط الجانبي',
    collapse: 'طي الشريط الجانبي',
  },
  nav: {
    dashboard: 'لوحة التحكم',
    users: 'المستخدمون',
    roles: 'الأدوار',
    roleAssignments: 'تعيين الأدوار',
    systemAgents: 'وكلاء النظام',
    aiProviders: 'مزودو الذكاء الاصطناعي',
    defaultModels: 'النماذج الافتراضية',
    aiCapabilities: 'قدرات الذكاء الاصطناعي',
    voice: 'الصوت',
    appearance: 'المظهر',
    agentPolicies: 'سياسات الوكلاء',
    workflowPolicies: 'سياسات سير العمل',
    mcpServers: 'خوادم MCP',
    operationalFailures: 'الأعطال التشغيلية',
    notifications: 'الإشعارات',
    deliveries: 'عمليات الإرسال',
    announcements: 'الإعلانات',
    templates: 'القوالب',
    localization: 'اللغات',
    jobs: 'المهام',
  },
  badges: {
    criticalCountError: 'تعذّر تحميل عدد الأعطال الحرجة غير المؤكَّدة',
    criticalCountErrorHidden: '(تعذّر تحميل عدد الأعطال الحرجة غير المؤكَّدة)',
    criticalCount: '({count} حرجة غير مؤكَّدة)',
    dictationSuspended: '(الإملاء الصوتي معلّق)',
  },
  jobs: {
    popupBlocked:
      'حظر المتصفح فتح لوحة المهام. اسمح بالنوافذ المنبثقة لهذا الموقع ثم حاول مرة أخرى.',
    openFailed: 'تعذّر فتح لوحة المهام. يرجى المحاولة مرة أخرى.',
  },
} satisfies MessagesOf<typeof enAdminShell>
