import type { MessagesOf } from '../../../types'
import type { enAdminAiTools } from '../../en/admin/aiTools'

export const arAdminAiTools = {
  title: 'أدوات الذكاء الاصطناعي',
  subtitle: 'الأدوات المتاحة للذكاء الاصطناعي — للقراءة فقط، ولا يمكن تعديلها من هنا',
  errors: { load: 'تعذّر تحميل أدوات الذكاء الاصطناعي.' },
  table: {
    name: 'الأداة',
    origin: 'المصدر',
    description: 'الوصف',
    risk: 'الخطورة',
    permissions: 'الصلاحيات المطلوبة',
    empty: 'لا توجد أدوات مسجَّلة حاليًا.',
  },
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
