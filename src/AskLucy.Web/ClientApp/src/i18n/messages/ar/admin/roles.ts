import type { MessagesOf } from '../../../types'
import type { enAdminRoles } from '../../en/admin/roles'

export const arAdminRoles = {
  title: 'الأدوار',
  bulk: { action: 'حذف', progress: 'جارٍ حذف' },
  subtitle: {
    zero: 'لا أدوار',
    one: 'دور واحد',
    two: 'دوران',
    few: '{count} أدوار',
    many: '{count} دورًا',
    other: '{count} دور',
  },
  createRole: 'إنشاء دور',
  search: 'البحث بالاسم',
  errors: {
    load: 'تعذّر تحميل الأدوار.',
    loadContentAccess: 'تعذّر تحميل ما إذا كان يحق للمشرفين عرض محتوى المستخدمين.',
    bulkPrepare: 'تعذّر تجهيز الحذف الجماعي. يرجى المحاولة مرة أخرى.',
  },
  contentAccess: { label: 'يحق للمشرفين عرض محتوى المستخدمين' },
  selection: {
    selectedCount: 'تم تحديد {count}',
    deleteSelected: 'حذف المحدد',
    selectAll: 'تحديد كل الأدوار المخصصة في هذه الصفحة',
    selectRole: 'تحديد {name}',
  },
  table: {
    name: 'الاسم',
    description: 'الوصف',
    permissions: 'الصلاحيات',
    users: 'المستخدمون',
    actions: 'الإجراءات',
    empty: 'لم يتم العثور على أدوار.',
    builtIn: 'مضمّن',
    default: 'افتراضي',
    defaultHint: 'الدور الأولي لكل حساب، وإليه يُنقل المستخدمون عند حذف دورهم',
    permissionCount: {
      zero: 'بلا صلاحيات',
      one: 'صلاحية واحدة',
      two: 'صلاحيتان',
      few: '{count} صلاحيات',
      many: '{count} صلاحية',
      other: '{count} صلاحية',
    },
    viewPermissions: 'عرض الصلاحيات',
    viewPermissionsFor: 'عرض صلاحيات {name}',
    actionsFor: 'إجراءات {name}',
  },
  pagination: {
    rowsPerPage: 'عدد الصفوف في الصفحة:',
    displayedRows: '{from}–{to} من {count}',
    first: 'الانتقال إلى الصفحة الأولى',
    previous: 'الانتقال إلى الصفحة السابقة',
    next: 'الانتقال إلى الصفحة التالية',
    last: 'الانتقال إلى الصفحة الأخيرة',
  },
  menu: {
    edit: 'تعديل…',
    duplicate: 'تكرار…',
    delete: 'حذف…',
    deleteSuperUserOnly: 'حذف (للمستخدم الخارق فقط)',
  },
  editor: {
    createTitle: 'إنشاء دور',
    editTitle: 'تعديل {name}',
    name: 'الاسم',
    description: 'الوصف',
    defaultNameHint: 'الدور الأولي لكل حساب — لا يمكن إعادة تسميته.',
    nameLength: 'يجب أن يتراوح اسم الدور بين 2 و50 حرفًا.',
    permissionRequired: 'اختر صلاحية واحدة على الأقل.',
    cancel: 'إلغاء',
    save: 'حفظ',
    create: 'إنشاء',
  },
  deleteDialog: {
    title: 'حذف {name}؟',
    usersMoved: {
      zero: 'لا يوجد مستخدمون سيُنقلون إلى دور {role}.',
      one: 'يشغل مستخدم واحد هذا الدور حاليًا وسيُنقل إلى دور {role}.',
      two: 'يشغل مستخدمان هذا الدور حاليًا وسيُنقلان إلى دور {role}.',
      few: 'يشغل {count} مستخدمين هذا الدور حاليًا وسيُنقلون إلى دور {role}.',
      many: 'يشغل {count} مستخدمًا هذا الدور حاليًا وسيُنقلون إلى دور {role}.',
      other: 'يشغل {count} مستخدم هذا الدور حاليًا وسيُنقلون إلى دور {role}.',
    },
    noUsers: 'لا يشغل أي مستخدم هذا الدور حاليًا.',
    irreversible: 'لا يمكن التراجع عن هذا الإجراء.',
    cancel: 'إلغاء',
    confirm: 'حذف',
  },
  duplicateDialog: {
    title: 'تكرار {name}',
    copyPrefix: 'نسخة من',
    noPermissions:
      'لا توجد صلاحيات في {name} لنسخها. أضف إليه صلاحية أولًا، أو أنشئ دورًا جديدًا بدلًا من ذلك.',
    summary: {
      zero: 'يحفظ دورًا مخصصًا جديدًا بصلاحيات {name}. لا يُنقل أي مستخدم إليه.',
      one: 'يحفظ دورًا مخصصًا جديدًا بصلاحية {name} الواحدة. لا يُنقل أي مستخدم إليه.',
      two: 'يحفظ دورًا مخصصًا جديدًا بصلاحيتَي {name}. لا يُنقل أي مستخدم إليه.',
      few: 'يحفظ دورًا مخصصًا جديدًا بصلاحيات {name} وعددها {count}. لا يُنقل أي مستخدم إليه.',
      many: 'يحفظ دورًا مخصصًا جديدًا بصلاحيات {name} وعددها {count}. لا يُنقل أي مستخدم إليه.',
      other: 'يحفظ دورًا مخصصًا جديدًا بصلاحيات {name} وعددها {count}. لا يُنقل أي مستخدم إليه.',
    },
    name: 'الاسم',
    description: 'الوصف',
    nameLength: 'يجب أن يتراوح اسم الدور بين 2 و50 حرفًا.',
    cancel: 'إلغاء',
    confirm: 'تكرار',
  },
  permissionsDialog: {
    title: 'صلاحيات {name}',
    none: 'لم تُمنح هذا الدور أي صلاحيات.',
    search: 'البحث في الصلاحيات',
    noMatch: 'لا توجد صلاحيات تطابق «{query}».',
    close: 'إغلاق',
  },
  picker: {
    superUserOnly: 'لا يمكن منح هذه الصلاحية إلا للمستخدم الخارق',
    basic: 'صلاحية أساسية — لا يمكن إزالتها من هذا الدور',
    levelView: 'عرض',
    levelManage: 'إدارة',
  },
  areas: {
    Dashboard: 'لوحة المعلومات',
    Users: 'المستخدمون',
    AiProviders: 'مزوّدو الذكاء الاصطناعي',
    DefaultModels: 'النماذج الافتراضية',
    AiCapabilities: 'قدرات الذكاء الاصطناعي',
    AgentPolicies: 'سياسات الوكلاء',
    SystemAgents: 'وكلاء النظام',
    WorkflowPolicies: 'سياسات سير العمل',
    McpServers: 'خوادم MCP',
    CustomModels: 'النماذج المخصصة',
    OperationalFailures: 'الإخفاقات التشغيلية',
    Notifications: 'الإشعارات',
    Appearance: 'المظهر',
  },
  permissions: {
    dashboardView: {
      name: 'عرض لوحة المعلومات',
      description: 'عرض النظرة العامة للوحة معلومات المشرف.',
    },
    usersView: { name: 'عرض المستخدمين', description: 'عرض قائمة المستخدمين وتفاصيلهم.' },
    usersManage: {
      name: 'إدارة المستخدمين',
      description: 'إنشاء المستخدمين أو تعديلهم أو تعليق حساباتهم أو حذفهم.',
    },
    aiProvidersView: {
      name: 'عرض مزوّدي الذكاء الاصطناعي',
      description: 'عرض إعدادات مزوّدي الذكاء الاصطناعي المسجّلين.',
    },
    aiProvidersManage: {
      name: 'إدارة مزوّدي الذكاء الاصطناعي',
      description: 'إضافة بيانات اعتماد مزوّدي الذكاء الاصطناعي أو تعديلها أو إزالتها.',
    },
    defaultModelsView: {
      name: 'عرض النماذج الافتراضية',
      description: 'عرض النموذج الافتراضي لكل مزوّد.',
    },
    defaultModelsManage: {
      name: 'إدارة النماذج الافتراضية',
      description: 'تغيير النموذج الافتراضي لأي مزوّد.',
    },
    aiCapabilitiesView: {
      name: 'عرض قدرات الذكاء الاصطناعي',
      description: 'عرض المزوّد الذي يخدم كل قدرة من قدرات الذكاء الاصطناعي.',
    },
    aiCapabilitiesManage: {
      name: 'إدارة قدرات الذكاء الاصطناعي',
      description: 'تغيير المزوّد الذي يخدم كل قدرة من قدرات الذكاء الاصطناعي.',
    },
    agentPoliciesView: {
      name: 'عرض سياسات الوكلاء',
      description: 'عرض إعدادات سياسات تنفيذ الوكلاء.',
    },
    agentPoliciesManage: {
      name: 'إدارة سياسات الوكلاء',
      description: 'إنشاء سياسات تنفيذ الوكلاء أو تعديلها.',
    },
    systemAgentsView: { name: 'عرض وكلاء النظام', description: 'عرض وكلاء النظام المسجّلين.' },
    workflowPoliciesView: {
      name: 'عرض سياسات سير العمل',
      description: 'عرض إعدادات سياسات تنفيذ سير العمل.',
    },
    workflowPoliciesManage: {
      name: 'إدارة سياسات سير العمل',
      description: 'إنشاء سياسات تنفيذ سير العمل أو تعديلها.',
    },
    mcpServersView: {
      name: 'عرض خوادم MCP',
      description: 'عرض خوادم بروتوكول سياق النموذج المسجّلة.',
    },
    mcpServersManage: {
      name: 'إدارة خوادم MCP',
      description: 'إضافة تسجيلات خوادم MCP أو تعديلها أو إزالتها.',
    },
    customModelsView: {
      name: 'عرض النماذج المخصصة',
      description: 'عرض عمليات نشر النماذج المخصصة وتقدمها.',
    },
    customModelsManage: {
      name: 'إدارة النماذج المخصصة',
      description:
        'نشر النماذج من Hugging Face إلى خادم الإنتاج، وإلغاء عمليات النشر، وتغيير توفر النماذج.',
    },
    operationalFailuresView: {
      name: 'عرض الإخفاقات التشغيلية',
      description:
        'عرض سجل الإخفاقات التشغيلية، مع روابط تتضمن البيانات الوصفية فقط إلى المحادثات وعمليات تشغيل سير العمل والمستندات المتأثرة.',
    },
    operationalFailuresManage: {
      name: 'إدارة الإخفاقات التشغيلية',
      description: 'الإقرار بحوادث الإخفاق التشغيلي وحلّها وإعادة فتحها.',
    },
    operationalFailuresContentView: {
      name: 'عرض محتوى المستخدم في تحقيقات الإخفاق',
      description:
        'قراءة المحتوى الكامل لمحادثة مستخدم آخر أو عملية تشغيل سير عمل أو مستند من حادثة إخفاق. يُدقَّق كل وصول. لا يمكن منحها أو سحبها إلا للمستخدم الخارق، ولا تمنح شيئًا دون صلاحية عرض الإخفاقات التشغيلية.',
    },
    notificationsView: {
      name: 'عرض الإشعارات',
      description:
        'عرض قوالب الإشعارات وسجل التسليم وحالات التسليم الفاشلة وسلامة القنوات وإعدادات التعريب.',
    },
    notificationsManage: {
      name: 'إدارة الإشعارات',
      description:
        'تعديل قوالب الإشعارات ونشرها، وإعادة محاولة التسليمات الفاشلة، ونشر إعلانات النظام، وتغيير إعدادات التعريب.',
    },
    appearanceView: {
      name: 'عرض المظهر',
      description: 'عرض إعدادات كرة الحضور ومعاينتها.',
    },
    appearanceManage: {
      name: 'إدارة المظهر',
      description: 'تغيير حجم نقاط كرة الحضور وحجمها داخل بطاقتها وإمكانية تكبيرها.',
    },
  },
} satisfies MessagesOf<typeof enAdminRoles>
