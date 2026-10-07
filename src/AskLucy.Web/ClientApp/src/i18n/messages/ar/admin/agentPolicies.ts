import type { MessagesOf } from '../../../types'
import type { enAdminAgentPolicies } from '../../en/admin/agentPolicies'

export const arAdminAgentPolicies = {
  page: {
    title: 'سياسات الوكلاء',
    subtitle: 'اعتمد مسبقًا إجراءات محددة عالية الخطورة للوكلاء لتعمل دون طلب موافقة تفاعلي',
  },
  panel: {
    newPolicy: 'سياسة جديدة',
    columns: {
      name: 'الاسم',
      tool: 'الأداة',
      conditions: 'الشروط',
      enabled: 'مفعّلة',
      actions: 'الإجراءات',
    },
    empty: 'لم يتم إعداد أي سياسات بعد.',
    always: 'دائمًا',
    enableAria: 'تفعيل {name}',
    deleteAria: 'حذف {name}',
    errors: {
      create: 'تعذّر إنشاء السياسة. يُرجى المحاولة مرة أخرى.',
      update: 'تعذّر تحديث السياسة. يُرجى المحاولة مرة أخرى.',
      delete: 'تعذّر حذف السياسة. يُرجى المحاولة مرة أخرى.',
    },
  },
  dialog: {
    title: 'سياسة جديدة',
    name: 'الاسم',
    toolName: 'اسم الأداة',
    toolNameHelp: 'يجب أن يطابق تمامًا الاسم المسجَّل للأداة، مثل ⁦FakeHighRiskTool⁩',
    description: 'الوصف',
    conditions: 'الشروط (JSON، اختيارية)',
    conditionsHelp:
      'كائن JSON مسطّح بقيم المعاملات المطلوبة، مثل ⁦{"action":"read-only"}⁩. اتركه فارغًا لمطابقة كل استدعاء لهذه الأداة.',
    cancel: 'إلغاء',
    create: 'إنشاء السياسة',
  },
} satisfies MessagesOf<typeof enAdminAgentPolicies>
