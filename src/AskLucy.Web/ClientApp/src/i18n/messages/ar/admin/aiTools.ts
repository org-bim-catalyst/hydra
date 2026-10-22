import type { MessagesOf } from '../../../types'
import type { enAdminAiTools } from '../../en/admin/aiTools'

export const arAdminAiTools = {
  title: 'أدوات الذكاء الاصطناعي',
  subtitle:
    'الأدوات المتاحة للذكاء الاصطناعي — الفهرس للقراءة فقط؛ أما الإعدادات الافتراضية لكل أداة فتكون خلف زر الإعدادات الخاص بها',
  errors: { load: 'تعذّر تحميل أدوات الذكاء الاصطناعي.' },
  table: {
    name: 'الأداة',
    origin: 'المصدر',
    description: 'الوصف',
    risk: 'الخطورة',
    permissions: 'الصلاحيات المطلوبة',
    empty: 'لا توجد أدوات مسجَّلة حاليًا.',
  },
  columns: {
    settings: 'الإعدادات',
  },
  row: {
    configure: 'إعداد {tool}',
    nothingToConfigure: 'لا شيء لإعداده',
    settingsFor: 'إعدادات {tool}',
  },
  dialog: {
    title: 'إعدادات {tool}',
    cancel: 'إلغاء',
    save: 'حفظ',
    saving: 'جارٍ الحفظ…',
    failed: 'حدث خطأ ما. يُرجى المحاولة مرة أخرى.',
    viewOnly:
      'يمكنك رؤية هذه الإعدادات لكن لا يمكنك تغييرها. يتطلب تغييرها إذن "إدارة أدوات الذكاء الاصطناعي".',
  },
  feedback: {
    settingsSaved: 'تم حفظ إعدادات الأداة.',
    failed: 'تعذّر حفظ الإعدادات. يُرجى المحاولة مرة أخرى.',
  },
  settingsLoadFailed:
    'تعذّر تحميل إعدادات الأدوات، لذا جميع أزرار الإعدادات معطّلة.',
  retry: 'إعادة المحاولة',
  risk: {
    Low: 'منخفضة',
    Medium: 'متوسطة',
    High: 'عالية',
    Critical: 'حرجة',
  },
  origin: {
    native: 'مدمجة',
    mcp: 'MCP',
  },
} satisfies MessagesOf<typeof enAdminAiTools>
