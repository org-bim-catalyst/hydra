import type { MessagesOf } from '../../../types'
import type { enAdminRoleAssignments } from '../../en/admin/roleAssignments'

export const arAdminRoleAssignments = {
  title: 'تعيينات الأدوار',
  bulk: { action: 'تعيين', progress: 'جارٍ تعيين الدور لـ' },
  subtitle: {
    zero: 'لا مستخدمين',
    one: 'مستخدم واحد',
    two: 'مستخدمان',
    few: '{count} مستخدمين',
    many: '{count} مستخدمًا',
    other: '{count} مستخدم',
  },
  search: 'البحث بالاسم أو البريد الإلكتروني',
  roleFilter: 'الدور',
  anyRole: 'أي دور',
  errors: {
    load: 'تعذّر تحميل تعيينات الأدوار.',
    bulkPrepare: 'تعذّر تجهيز التعيين الجماعي. يرجى المحاولة مرة أخرى.',
  },
  selection: {
    selectedCount: 'تم تحديد {count}',
    assignSelected: 'تعيين المحدد',
    selectAll: 'تحديد كل المستخدمين المؤهلين في هذه الصفحة',
    selectUser: 'تحديد {email}',
  },
  table: {
    email: 'البريد الإلكتروني',
    name: 'الاسم',
    role: 'الدور',
    status: 'الحالة',
    actions: 'الإجراءات',
    empty: 'لم يتم العثور على تعيينات أدوار.',
    locked: 'مقفل',
    active: 'نشط',
    changeRole: 'تغيير الدور…',
    defaultRole: 'مستخدم',
  },
  pagination: {
    rowsPerPage: 'عدد الصفوف في الصفحة:',
    displayedRows: '{from}–{to} من {count}',
    first: 'الانتقال إلى الصفحة الأولى',
    previous: 'الانتقال إلى الصفحة السابقة',
    next: 'الانتقال إلى الصفحة التالية',
    last: 'الانتقال إلى الصفحة الأخيرة',
  },
  roleOption: {
    plain: '{name}',
    builtIn: '{name} (مضمّن)',
    superUserOnly: '{name} (للمستخدم الخارق فقط)',
    builtInSuperUserOnly: '{name} (مضمّن) (للمستخدم الخارق فقط)',
  },
  dialog: {
    title: 'تغيير دور {email}',
    replaces: 'تعيين دور جديد يستبدل الدور الحالي للمستخدم — إذ لا يشغل المستخدم أكثر من دور واحد.',
    locked:
      'يتضمن دور هذا المستخدم صلاحية عرض محتوى المستخدمين. لا يمكن تغييره إلا للمستخدم الخارق.',
    role: 'الدور',
    cancel: 'إلغاء',
    save: 'حفظ',
  },
} satisfies MessagesOf<typeof enAdminRoleAssignments>
