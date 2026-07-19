import { WorkerManagementProfile } from './worker-management.models';

export const WORKER_MANAGEMENT_FIXTURES: readonly WorkerManagementProfile[] = [
  {
    id: 'worker-local-ar-source-en',
    local: {
      displayName: 'أحمد محمود علي',
      photoUrl: null,
      salary: { amount: 8650, currencyCode: 'EGP', effectiveFrom: '2026-06-01' },
      profileStatus: 'complete',
      employmentStatus: 'active'
    },
    source: {
      sourceName: 'Ahmed Mahmoud Ali',
      badgeNumber: 'B-1042',
      employeeCode: 'EMP-1042',
      employmentStatus: 'Active (observed)',
      department: 'Sewing',
      shift: 'Morning A',
      lastObservedAt: '2026-07-19T05:42:00Z',
      linkStatus: 'linked'
    },
    assignments: [
      { id: 'a-1', kind: 'permanent', factoryId: 'factory-a', factoryName: 'مصنع التجميع', productionLineId: 'line-a1', productionLineName: 'خط الخياطة 1', stageNames: ['تجهيز الجسم', 'الخياطة النهائية'], periodLabel: 'تسكين أساسي حالي' }
    ],
    history: [
      { id: 'h-1', kind: 'name', title: 'تحديث الاسم المحلي', detail: 'تم اعتماد الاسم العربي المعروض في ملف العامل.', occurredAt: '2026-07-15T09:15:00Z', actorLabel: 'إدارة الموارد' },
      { id: 'h-2', kind: 'assignment', title: 'تغيير التسكين الأساسي', detail: 'انتقل إلى خط الخياطة 1.', occurredAt: '2026-07-10T07:30:00Z', actorLabel: 'مشرف التشغيل' }
    ],
    sourcePreview: [
      { id: 'p-1', kind: 'unchanged', title: 'BadgeNumber', detail: 'مطابق لآخر قراءة مرصودة.' },
      { id: 'p-2', kind: 'protected-local', title: 'الاسم المحلي والراتب', detail: 'لن تستبدلهما بيانات المصدر تلقائيًا.' },
      { id: 'p-3', kind: 'observed', title: 'القسم والشيفت', detail: 'معلومات مرصودة للعرض والمراجعة فقط.' }
    ]
  },
  {
    id: 'worker-mixed-assignment',
    local: { displayName: 'سلمى حسين', photoUrl: null, salary: null, profileStatus: 'needs-review', employmentStatus: 'active' },
    source: { sourceName: 'Salma Hussein', badgeNumber: 'B-2050', employeeCode: 'EMP-2050', employmentStatus: 'Active (observed)', department: 'Finishing', shift: 'Evening B', lastObservedAt: '2026-07-19T05:40:00Z', linkStatus: 'linked' },
    assignments: [
      { id: 'a-2', kind: 'permanent', factoryId: 'factory-a', factoryName: 'مصنع التجميع', productionLineId: 'line-a2', productionLineName: 'خط التشطيب', stageNames: ['تشطيب الحواف'], periodLabel: 'تسكين أساسي حالي' },
      { id: 'a-3', kind: 'temporary', factoryId: 'factory-b', factoryName: 'مصنع التعبئة', productionLineId: 'line-b1', productionLineName: 'خط التعبئة 2', stageNames: ['فحص الجودة', 'التعبئة'], periodLabel: 'نقل مؤقت حتى 22 يوليو' }
    ],
    history: [{ id: 'h-3', kind: 'assignment', title: 'بدء نقل مؤقت', detail: 'إضافة مساندة مؤقتة لخط التعبئة 2.', occurredAt: '2026-07-18T06:00:00Z', actorLabel: 'مدير الوردية' }],
    sourcePreview: [{ id: 'p-4', kind: 'protected-local', title: 'التسكين', detail: 'التسكين التشغيلي محلي ولن يتغير من بيانات المصدر.' }]
  },
  {
    id: 'worker-unassigned-missing-source',
    local: { displayName: 'يوسف عادل', photoUrl: null, salary: null, profileStatus: 'needs-review', employmentStatus: 'active' },
    source: { sourceName: 'Youssef Adel', badgeNumber: 'B-3100', employeeCode: null, employmentStatus: null, department: null, shift: null, lastObservedAt: '2026-07-18T05:30:00Z', linkStatus: 'linked' },
    assignments: [],
    history: [{ id: 'h-4', kind: 'status', title: 'إنشاء ملف محلي', detail: 'أُنشئ الملف وما زال التسكين قيد المراجعة.', occurredAt: '2026-07-17T10:00:00Z', actorLabel: 'النظام التجريبي' }],
    sourcePreview: [{ id: 'p-5', kind: 'observed', title: 'بيانات مصدر ناقصة', detail: 'القسم والشيفت وحالة المصدر غير متاحة في آخر قراءة.' }]
  },
  {
    id: 'worker-identity-conflict',
    local: { displayName: 'هدى إبراهيم سالم', photoUrl: null, salary: { amount: 9100, currencyCode: 'EGP', effectiveFrom: '2026-05-01' }, profileStatus: 'needs-review', employmentStatus: 'active' },
    source: { sourceName: 'Hoda E. Saleh', badgeNumber: 'B-4108', employeeCode: 'EMP-9991', employmentStatus: 'Active (observed)', department: 'Assembly', shift: 'Morning A', lastObservedAt: '2026-07-19T05:38:00Z', linkStatus: 'conflict' },
    assignments: [{ id: 'a-4', kind: 'permanent', factoryId: 'factory-a', factoryName: 'مصنع التجميع', productionLineId: 'line-a1', productionLineName: 'خط الخياطة 1', stageNames: ['التجميع'], periodLabel: 'تسكين أساسي حالي' }],
    history: [{ id: 'h-5', kind: 'name', title: 'رصد اختلاف في الهوية', detail: 'الاسم وكود الموظف المرصودان يحتاجان مراجعة بشرية.', occurredAt: '2026-07-19T05:38:00Z', actorLabel: 'معاينة المصدر' }],
    sourcePreview: [{ id: 'p-6', kind: 'identity-conflict', title: 'الاسم وكود الموظف', detail: 'لن يُطبق أي تغيير حتى تكتمل مراجعة الهوية مستقبلًا.' }]
  },
  {
    id: 'worker-new-from-source',
    local: { displayName: 'عامل جديد بانتظار المراجعة', photoUrl: null, salary: null, profileStatus: 'source-pending', employmentStatus: 'not-set' },
    source: { sourceName: 'Mariam Nabil', badgeNumber: 'B-5201', employeeCode: 'EMP-5201', employmentStatus: 'Active (observed)', department: 'Cutting', shift: 'Morning A', lastObservedAt: '2026-07-19T05:45:00Z', linkStatus: 'new-source' },
    assignments: [],
    history: [{ id: 'h-6', kind: 'status', title: 'ظهر سجل جديد في المصدر', detail: 'لم يُراجع الملف محليًا ولم تُطبق حالة عمل محلية.', occurredAt: '2026-07-19T05:45:00Z', actorLabel: 'معاينة المصدر' }],
    sourcePreview: [{ id: 'p-7', kind: 'new', title: 'هوية مصدر جديدة', detail: 'تحتاج مراجعة قبل إنشاء أو ربط أي بيانات محلية مستقبلًا.' }]
  },
  {
    id: 'worker-long-arabic-name',
    local: { displayName: 'عبد الرحمن محمد عبد السلام أحمد مصطفى الطويل لاختبار وضوح الأسماء العربية الممتدة', photoUrl: null, salary: { amount: 11250, currencyCode: 'EGP', effectiveFrom: '2026-04-01' }, profileStatus: 'complete', employmentStatus: 'active' },
    source: { sourceName: 'Abdelrahman Mohamed Abdelsalam Ahmed Mostafa Al-Taweel', badgeNumber: 'B-6008-LONG', employeeCode: 'EMP-6008-LONG', employmentStatus: 'Active (observed)', department: 'Quality Assurance and Final Inspection', shift: 'Extended Rotating Shift', lastObservedAt: '2026-07-19T05:39:00Z', linkStatus: 'linked' },
    assignments: [{ id: 'a-5', kind: 'permanent', factoryId: 'factory-b', factoryName: 'مصنع التعبئة', productionLineId: 'line-b1', productionLineName: 'خط التعبئة 2', stageNames: ['فحص الجودة النهائي'], periodLabel: 'تسكين أساسي حالي' }],
    history: [{ id: 'h-7', kind: 'photo', title: 'لا توجد صورة محلية', detail: 'يُعرض البديل القياسي إلى أن تتوفر معالجة الصور مستقبلًا.', occurredAt: '2026-07-01T08:00:00Z', actorLabel: 'ملف العامل' }],
    sourcePreview: [{ id: 'p-8', kind: 'unchanged', title: 'الربط', detail: 'الهوية الخارجية مستقرة في المعاينة الحالية.' }]
  },
  {
    id: 'worker-many-stages',
    local: { displayName: 'كريم فتحي', photoUrl: null, salary: null, profileStatus: 'complete', employmentStatus: 'active' },
    source: { sourceName: 'Karim Fathy', badgeNumber: 'B-7014', employeeCode: 'EMP-7014', employmentStatus: 'Active (observed)', department: 'Multi Skills', shift: 'Flexible', lastObservedAt: '2026-07-19T05:41:00Z', linkStatus: 'linked' },
    assignments: [{ id: 'a-6', kind: 'permanent', factoryId: 'factory-a', factoryName: 'مصنع التجميع', productionLineId: 'line-a2', productionLineName: 'خط التشطيب', stageNames: ['تجهيز', 'تشغيل', 'مراجعة أولية', 'فحص نهائي', 'تعبئة', 'تسليم'], periodLabel: 'تسكين متعدد المراحل' }],
    history: [{ id: 'h-8', kind: 'assignment', title: 'توسيع نطاق المراحل', detail: 'أضيفت مرحلتا الفحص النهائي والتسليم.', occurredAt: '2026-07-16T11:20:00Z', actorLabel: 'مشرف الخط' }],
    sourcePreview: [{ id: 'p-9', kind: 'protected-local', title: 'المراحل التشغيلية', detail: 'تبقى ضمن إدارة ProductionLinePlanner.' }]
  },
  {
    id: 'worker-missing-from-source',
    local: { displayName: 'نور صبري', photoUrl: null, salary: { amount: 7800, currencyCode: 'EGP', effectiveFrom: '2026-03-01' }, profileStatus: 'needs-review', employmentStatus: 'active' },
    source: { sourceName: 'Nour Sabry', badgeNumber: 'B-8090', employeeCode: 'EMP-8090', employmentStatus: null, department: null, shift: null, lastObservedAt: '2026-07-16T05:33:00Z', linkStatus: 'missing-source' },
    assignments: [{ id: 'a-7', kind: 'permanent', factoryId: 'factory-b', factoryName: 'مصنع التعبئة', productionLineId: 'line-b1', productionLineName: 'خط التعبئة 2', stageNames: ['التعبئة'], periodLabel: 'تسكين أساسي حالي' }],
    history: [{ id: 'h-9', kind: 'status', title: 'غير ظاهر في آخر قراءة', detail: 'لم تتغير حالة العمل المحلية تلقائيًا.', occurredAt: '2026-07-19T05:33:00Z', actorLabel: 'معاينة المصدر' }],
    sourcePreview: [{ id: 'p-10', kind: 'observed', title: 'غياب عن القراءة الأخيرة', detail: 'لا يعني ترك العمل ولا يغير الحالة المحلية.' }]
  }
];
