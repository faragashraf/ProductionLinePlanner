# Release Strategy

## المراحل

- **Checkpoint**
  - تحقق داخلي لكل capability بعد Validation.
  - يعتمد قبل تحويل أي capability إلى Release candidate.

- **Release**
  - نشر Capability بعد اكتمال Checkpoint.
  - يتضمن توثيق notes موجزة ومرجع الاختبارات المرتبط.

- **Hardening**
  - تنفيذ تحسينات الأمان/الأداء/الموثوقية بعد release أو قبله (حسب nature الخلل).
  - الـ hardening غير تجاري في جوهره، ولا يبدّل business behavior إلا بعد إعادة review.

## قواعد مطلوبة

- لا يوجد release بدون سجل checkpoint واضح.
- أي defect عالي الخطورة يظهر بعد release يدخل في hardening ثم إعادة قياس.
- المراقبة (monitoring) وتأكيد smoke checks تكون جزءًا من checklist release.
