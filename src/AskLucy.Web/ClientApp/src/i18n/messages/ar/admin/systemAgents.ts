import type { MessagesOf } from '../../../types'
import type { enAdminSystemAgents } from '../../en/admin/systemAgents'

export const arAdminSystemAgents = {
  title: 'وكلاء النظام',
  subtitle: 'الوكلاء الذين توفّرهم المنصة بنفسها — للقراءة فقط، ولا يمكن للمستخدمين تعديلهم',
  errors: { load: 'تعذّر تحميل وكلاء النظام.' },
  table: {
    name: 'الاسم',
    origin: 'المصدر',
    systemKey: 'مفتاح النظام',
    status: 'الحالة',
    version: 'الإصدار',
    lastUpdated: 'آخر تحديث',
    empty: 'لا يوجد وكلاء نظام مُهيَّؤون حاليًا.',
    provisioned: 'مُهيَّأ بواسطة Ask Lucy',
  },
  status: {
    Draft: 'مسودة',
    Published: 'منشور',
    Archived: 'مؤرشف',
  },
} satisfies MessagesOf<typeof enAdminSystemAgents>
