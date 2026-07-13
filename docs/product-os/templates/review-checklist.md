# Review Checklist Template

## Capability Review

- Capability name:
- Reviewer:
- Date:
- Branch:

## نقاط المراجعة

### Architecture
- [ ] هل التصميم متوافق مع الأجزاء القائمة؟
- [ ] هل تم تجنب duplication؟
- [ ] هل هناك coupling خطر؟

### Security
- [ ] Authorization في backend صحيح؟
- [ ] أي تجاوزات للصلاحيات/الأدوار؟
- [ ] هل هناك حماية كافية لبيانات حساسة؟

### Implementation Quality
- [ ] Endpoints رقيقة؟
- [ ] business logic داخل الخدمة/Engine؟
- [ ] تغطية اختبارات مقبولة؟

### UX and Product
- [ ] سلوك الواجهة مطابق للتوقع؟
- [ ] حالات التحميل والأخطاء واضحة؟
- [ ] هناك regression visible؟

### Final
- [ ] هل يمكن اعتماد الـ Merge؟
- [ ] هل يلزم hardening إضافي قبل release؟
- [ ] هل تم وضع ملاحظات واضحة لكل عنصر لم يُغلق؟
