import type { MessagesOf } from '../../../types'
import type { enAdminDefaultModels } from '../../en/admin/defaultModels'

export const arAdminDefaultModels = {
  title: 'النماذج الافتراضية',
  subtitle: 'النموذج الذي يقدّمه كل مزوّد — تعمل القدرة المسندة إلى مزوّد على النموذج المحدد هنا',
  columns: {
    provider: 'المزوّد',
    status: 'الحالة',
    defaultModel: 'النموذج الافتراضي',
  },
  empty: 'لا يوجد مزوّدون قابلون للإسناد.',
  info: 'لا يظهر هنا إلا المزوّدون المفعّلون الذين لديهم بيانات اعتماد، ولا يمكن أن يكون نموذجًا افتراضيًا إلا ما عُلّم بأنه «متاح» في صفحة المزوّدين. لا يمكن إسناد مزوّد بلا نموذج افتراضي إلى قدرة.',
  feedback: {
    cleared: 'تمت إزالة النموذج الافتراضي.',
    saved: 'تم حفظ النموذج الافتراضي.',
    failed: 'حدث خطأ ما. يُرجى المحاولة مرة أخرى.',
  },
  row: {
    enabled: 'مفعّل',
    disabled: 'معطّل',
    noDefault: 'بلا نموذج افتراضي',
    defaultModelFor: 'النموذج الافتراضي لـ {provider}',
    modelsLoadFailed: 'تعذّر تحميل نماذج هذا المزوّد. أعد تحميل الصفحة للمحاولة مرة أخرى.',
    noAvailableModels: 'لا توجد نماذج متاحة — علّم نموذجًا كمتاح في صفحة المزوّدين أولًا.',
  },
} satisfies MessagesOf<typeof enAdminDefaultModels>
