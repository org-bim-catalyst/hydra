import type { MessagesOf } from '../../types'
import type { enCommon } from '../en/common'

export const arCommon = {
  actions: {
    retry: 'إعادة المحاولة',
    reload: 'إعادة التحميل',
    delete: 'حذف',
    close: 'إغلاق',
    save: 'حفظ',
  },
  states: {
    loading: 'جارٍ التحميل…',
    loadingPleaseWait: 'جارٍ التحميل… يرجى الانتظار',
    noLongerAvailable: 'لم يعد متاحًا',
  },
  errors: {
    generic: 'حدث خطأ ما. يرجى المحاولة مرة أخرى.',
  },
} satisfies MessagesOf<typeof enCommon>
