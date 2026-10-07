import type { MessagesOf } from '../../../types'
import type { enAdminWorkflowPolicies } from '../../en/admin/workflowPolicies'

export const arAdminWorkflowPolicies = {
  page: {
    title: 'سياسات سير العمل',
    subtitle: 'اعتمد مسبقًا خطوات محددة عالية الخطورة في سير العمل لتعمل دون طلب موافقة تفاعلي',
  },
  panel: {
    newPolicy: 'سياسة جديدة',
    columns: {
      name: 'الاسم',
      nodeType: 'نوع العقدة',
      underlyingTool: 'الأداة الأساسية',
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
  nodeTypes: {
    AiPrompt: 'مطالبة ذكاء اصطناعي',
    AiAgent: 'وكيل ذكاء اصطناعي',
    RagSearch: 'بحث RAG',
    MemorySearch: 'بحث في الذاكرة',
    DocumentProcessing: 'معالجة المستندات',
    FileOperation: 'عملية على الملفات',
    McpTool: 'أداة MCP',
    NativeTool: 'أداة أصلية',
    HumanApproval: 'موافقة بشرية',
  },
  dialog: {
    title: 'سياسة جديدة',
    name: 'الاسم',
    nodeType: 'نوع العقدة (اختياري)',
    nodeTypeHelp: 'اتركه دون تحديد للاستهداف باسم الأداة الأساسية فقط',
    none: 'لا شيء',
    underlyingToolName: 'اسم الأداة الأساسية (اختياري)',
    underlyingToolNameHelp:
      'يجب أن يطابق تمامًا اسم الأداة المسجَّل للإمكانية الأساسية، مثل ⁦KnowledgeSearchTool⁩',
    description: 'الوصف',
    conditions: 'الشروط (JSON، اختيارية)',
    conditionsHelp:
      'كائن JSON مسطّح بقيم المعاملات المطلوبة، مثل ⁦{"visibility":"public"}⁩. اتركه فارغًا لمطابقة كل عقدة مطابقة.',
    cancel: 'إلغاء',
    create: 'إنشاء السياسة',
  },
} satisfies MessagesOf<typeof enAdminWorkflowPolicies>
