import type { MessagesOf } from '../../../types'
import type { enAdminAiCapabilities } from '../../en/admin/aiCapabilities'

export const arAdminAiCapabilities = {
  title: 'قدرات الذكاء الاصطناعي',
  subtitle: 'اختر المزوّد والنموذج الذي يخدم كل قدرة',
  columns: {
    capability: 'القدرة',
    provider: 'المزوّد المُسنَد',
    model: 'النموذج',
    settings: 'الإعدادات',
  },
  empty: 'لا توجد قدرات.',
  info: 'تعمل كل قدرة على المزوّد المُسنَد إليها هنا. اترك نموذجها على «افتراضي المزوّد» ليتبع النموذج الافتراضي لذلك المزوّد، أو اختر نموذجًا آخر من نماذجه لهذه القدرة وحدها. يجب أن يكون نموذج توليد الصور قادرًا على إنتاج الصور: فالنموذج الافتراضي للمزوّد نموذج محادثة ولا يستطيع الرسم.',
  settingsLoadFailed: 'تعذّر تحميل إعدادات القدرات، لذا جميع أزرار الإعدادات معطّلة.',
  retry: 'إعادة المحاولة',
  noProviderAssignable:
    'لا يمكن إسناد أي مزوّد بعد. فعّل مزوّدًا مع بيانات اعتماده في صفحة المزوّدين، ثم حدّد له نموذجًا افتراضيًا في صفحة النماذج الافتراضية.',
  feedback: {
    assignmentSaved: 'تم حفظ إسناد القدرة.',
    settingsSaved: 'تم حفظ إعدادات القدرة.',
    failed: 'حدث خطأ ما. يُرجى المحاولة مرة أخرى.',
  },
  capabilities: {
    unknown: {
      consequence: 'لا يتوفر وصف لهذه القدرة بعد.',
    },
    Chat: {
      label: 'المحادثة',
      consequence: 'يردّ على المستخدم أثناء المحادثة. جميع القدرات الأخرى هنا أعمال في الخلفية.',
    },
    LocationIntent: {
      label: 'قصد الموقع',
      consequence: 'يحدد ما إذا كانت الرسالة تطلب عرض مكان. وبدونه لا ينتقل العارض أبدًا.',
    },
    MemoryExtraction: {
      label: 'استخراج الذاكرة',
      consequence: 'يقرأ المحادثات المنتهية بحثًا عن حقائق تستحق التذكّر.',
    },
    MemoryConflictDetection: {
      label: 'اكتشاف تعارض الذاكرة',
      consequence: 'يحدد ما إذا كانت ذاكرة جديدة تناقض ذاكرة محفوظة.',
    },
    DocumentClassification: {
      label: 'لغة المستند وتصنيفه',
      consequence: 'يكتشف لغة المستند المرفوع ونوعه.',
    },
    BoundaryVision: {
      label: 'رؤية حدود الموقع',
      consequence:
        'يتحقق من حدود الموقع بمقارنتها بصور الأقمار الصناعية. يتطلب حاليًا Google Gemini.',
    },
    TurnOrchestration: {
      label: 'تنسيق الأدوار',
      consequence: 'يحدد ما يحتاجه كل دور في المحادثة — هل يلزم اتخاذ إجراء، وأي قدرة تُشغَّل.',
    },
    ImageGeneration: {
      label: 'توليد الصور',
      consequence:
        'يرسم صورًا من وصف نصي — صور المحادثة وخريطة تحليل الموقع. يحتاج إلى نموذج قادر على إنتاج الصور، ولا يعود أبدًا إلى نموذج محادثة.',
    },
  },
  row: {
    providerFor: 'المزوّد لقدرة {capability}',
    modelFor: 'النموذج لقدرة {capability}',
    noProviderAvailable: 'لا يوجد مزوّد ذكاء اصطناعي متاح',
    selectProvider: 'يُرجى اختيار مزوّد الذكاء الاصطناعي',
    assignProviderFirst: 'أسند مزوّدًا أولًا',
    providerDefault: 'افتراضي المزوّد',
    providerDefaultWithModel: 'افتراضي المزوّد · {model}',
    loadingModels: 'جارٍ تحميل النماذج…',
    selectImageModel: 'يُرجى اختيار نموذج الصور',
    modelsLoadFailed: 'تعذّر تحميل نماذج هذا المزوّد. أعد تحميل الصفحة للمحاولة مرة أخرى.',
    noImageModel:
      'لا يملك هذا المزوّد نموذجًا متاحًا معلَّمًا بأنه قادر على إنتاج الصور. أضف نموذجًا أو فعّله في صفحة النماذج.',
    noAvailableModel: 'لا يملك هذا المزوّد نموذجًا متاحًا. علّم نموذجًا كمتاح في صفحة النماذج.',
    configure: 'إعداد {capability}',
    nothingToConfigure: 'لا شيء لإعداده',
    settingsFor: 'إعدادات {capability}',
  },
  dialog: {
    title: 'إعدادات {capability}',
    cancel: 'إلغاء',
    save: 'حفظ',
    saving: 'جارٍ الحفظ…',
    failed: 'حدث خطأ ما. يُرجى المحاولة مرة أخرى.',
  },
} satisfies MessagesOf<typeof enAdminAiCapabilities>
