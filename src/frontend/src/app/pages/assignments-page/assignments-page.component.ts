import { Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { deriveStatusFromReadiness, FactoryStatus } from '../../shared/models/factory-status.model';

interface AssignmentItem {
  worker: string;
  from: string;
  to: string;
  type: 'ثابت' | 'مؤقت';
  status: FactoryStatus;
}

interface AssignmentWorker {
  id: string;
  code: string;
  fullName: string;
  status: FactoryStatus;
  assignmentType: 'ثابت' | 'مؤقت' | 'غير محدد';
  line: string;
  subStage: string;
  lastActivity: string;
}

interface SubStageDropZone {
  id: string;
  line: string;
  name: string;
  workersCurrent: number;
  workersRequired: number;
  status: FactoryStatus;
  workerIds: string[];
}

interface RecommendationCandidate {
  workerName: string;
  score: number;
  from: string;
  to: string;
  reasons: string[];
  risks: string[];
  targetLine: string;
  targetStage: string;
}

interface TimelineEntry {
  time: string;
  title: string;
  details: string;
  status: FactoryStatus;
}

interface StageDemoContext {
  lineId: string;
  lineName: string;
  stageId: string;
  stageName: string;
  source: 'factory-map' | 'manual';
}

interface TemporaryDialogState {
  isOpen: boolean;
  targetZoneId: string;
  targetZoneLabel: string;
  selectedWorkerId: string | null;
}

interface ReplacementDialogState {
  isOpen: boolean;
  targetWorkerId: string | null;
  replacementWorkerId: string | null;
}

@Component({
  selector: 'app-assignments-page',
  templateUrl: './assignments-page.component.html',
  styleUrls: ['./assignments-page.component.scss']
})
export class AssignmentsPageComponent implements OnInit {
  demoContext: StageDemoContext | null = null;

  constructor(private readonly route: ActivatedRoute) {}

  ngOnInit(): void {
    this.initializeDemoContext();
  }
  assignments: AssignmentItem[] = [
    {
      worker: 'أحمد سعيد',
      from: 'الخط الأحمر - مرحلة الخلط',
      to: 'الخط الأحمر - مرحلة التغليف',
      type: 'ثابت',
      status: 'ready'
    },
    {
      worker: 'سارة علي',
      from: 'غير محدد',
      to: 'الخط الأزرق - مرحلة التغذية',
      type: 'مؤقت',
      status: 'warning'
    }
  ];

  subStageZones: SubStageDropZone[] = [
    {
      id: 'red-mix',
      line: 'الخط الأحمر',
      name: 'مرحلة الخلط',
      workersCurrent: 2,
      workersRequired: 4,
      status: 'ready',
      workerIds: ['w1', 'w2']
    },
    {
      id: 'red-pack',
      line: 'الخط الأحمر',
      name: 'مرحلة التغليف',
      workersCurrent: 1,
      workersRequired: 5,
      status: 'warning',
      workerIds: ['w6']
    },
    {
      id: 'blue-feed',
      line: 'الخط الأزرق',
      name: 'مرحلة التغذية',
      workersCurrent: 1,
      workersRequired: 4,
      status: 'warning',
      workerIds: ['w5']
    },
    {
      id: 'blue-test',
      line: 'الخط الأزرق',
      name: 'مرحلة فحص الجودة',
      workersCurrent: 0,
      workersRequired: 3,
      status: 'critical',
      workerIds: []
    }
  ];

  workers: AssignmentWorker[] = [
    {
      id: 'w1',
      code: 'W-101',
      fullName: 'أحمد سعيد',
      status: 'ready',
      assignmentType: 'ثابت',
      line: 'الخط الأحمر',
      subStage: 'مرحلة الخلط',
      lastActivity: 'تعيين ثابت منذ 02:13'
    },
    {
      id: 'w2',
      code: 'W-102',
      fullName: 'سارة علي',
      status: 'late',
      assignmentType: 'مؤقت',
      line: 'الخط الأحمر',
      subStage: 'مرحلة الخلط',
      lastActivity: 'تم تحويلها مؤقتًا قبل 18 دقيقة'
    },
    {
      id: 'w3',
      code: 'W-106',
      fullName: 'محمود يونس',
      status: 'present',
      assignmentType: 'ثابت',
      line: 'الخط الأحمر',
      subStage: 'مرحلة فحص',
      lastActivity: 'جاهز لإعادة التوزيع'
    },
    {
      id: 'w4',
      code: 'W-115',
      fullName: 'نوال مرتضى',
      status: 'warning',
      assignmentType: 'غير محدد',
      line: 'غير محدد',
      subStage: 'غير محدد',
      lastActivity: 'متاحة للتعيين'
    },
    {
      id: 'w5',
      code: 'W-121',
      fullName: 'إسماعيل زيد',
      status: 'absent',
      assignmentType: 'غير محدد',
      line: 'غير محدد',
      subStage: 'غير محدد',
      lastActivity: 'غياب قصير'
    },
    {
      id: 'w6',
      code: 'W-134',
      fullName: 'رنا مراد',
      status: 'ready',
      assignmentType: 'ثابت',
      line: 'الخط الأحمر',
      subStage: 'مرحلة التغليف',
      lastActivity: 'انتقلت من الخط الأزرق قبل ساعة'
    }
  ];

  recommendations: RecommendationCandidate[] = [
    {
      workerName: 'نوال مرتضى',
      score: 97,
      from: 'غير محدد',
      to: 'الخط الأحمر - مرحلة التغليف',
      reasons: [
        'مهارة تغليف معتمدة من الوردية السابقة',
        'حالة حضور ممتازة اليوم',
        'سرعة مناسبة في تغيير المهام'
      ],
      risks: ['فترة توقف قصيرة متوقعة قبل بداية الوردية'],
      targetLine: 'الخط الأحمر',
      targetStage: 'مرحلة التغليف'
    },
    {
      workerName: 'محمود يونس',
      score: 93,
      from: 'غير محدد',
      to: 'الخط الأحمر - مرحلة الخلط',
      reasons: [
        'مستوى أداء ثابت',
        'يتقن مرحلة الخلط',
        'عدد تغيّب منخفض'
      ],
      risks: ['إذا تم نقله الآن قد يتأخر التغطية على فحص الجودة'],
      targetLine: 'الخط الأحمر',
      targetStage: 'مرحلة الخلط'
    },
    {
      workerName: 'إسماعيل زيد',
      score: 88,
      from: 'الغياب المؤقت',
      to: 'الخط الأزرق - مرحلة التغذية',
      reasons: [
        'معرفة عالية بأجهزة التغذية',
        'يمكن تغطيته في 20 دقيقة'
      ],
      risks: ['تتطلب عودة العامل بعد 20 دقيقة لتثبيت التغييرات'],
      targetLine: 'الخط الأزرق',
      targetStage: 'مرحلة تغذية'
    }
  ];

  timelineEntries: TimelineEntry[] = [
    {
      time: '09:00',
      title: 'تحميل واجهة التعيينات',
      details: 'تم تحميل صفحة التعيينات ببيانات mock فقط للتجريب الداخلي.',
      status: 'info'
    },
    {
      time: '09:08',
      title: 'تفعيل منطقة Drop Zone',
      details: 'تم عرض مراحل خلط وتغذية وتغليف وفحص كمناطق سحب mock.',
      status: 'ready'
    },
    {
      time: '09:14',
      title: 'محاكاة تعيين مؤقت',
      details: 'مكون مربع التعيين المؤقت جاهز لعرض مسار الاختبار.',
      status: 'warning'
    },
    {
      time: '09:27',
      title: 'تحذير مخاطرة التعديل',
      details: 'مرشح بديل تم تصنيفه، وتم تمرير المخاطر قبل التأكيد الحقيقي.',
      status: 'critical'
    }
  ];

  temporaryDialog: TemporaryDialogState = {
    isOpen: false,
    targetZoneId: 'red-mix',
    targetZoneLabel: '',
    selectedWorkerId: null
  };

  replacementDialog: ReplacementDialogState = {
    isOpen: false,
    targetWorkerId: null,
    replacementWorkerId: null
  };

  lastSimulationMessage = '';

  get hasContextSelection(): boolean {
    return !!this.demoContext;
  }

  get selectedShortageZone(): SubStageDropZone | undefined {
    if (!this.demoContext) {
      return undefined;
    }
    return (
      this.subStageZones.find(
        (zone) => zone.line === this.demoContext!.lineName && zone.name === this.demoContext!.stageName
      ) ??
      this.subStageZones.find((zone) => zone.line === this.demoContext!.lineName)
    );
  }

  get selectedShortageLabel(): string {
    if (!this.demoContext) {
      return 'يرجى اختيار مرحلة لربط التوصية';
    }
    return `${this.demoContext.lineName} - ${this.demoContext.stageName}`;
  }

  get selectedShortageReadiness(): number {
    const zone = this.selectedShortageZone;
    if (!zone || zone.workersRequired <= 0) {
      return 0;
    }
    return Math.round((zone.workersCurrent / zone.workersRequired) * 100);
  }

  get selectedShortageTone(): FactoryStatus {
    return this.hasContextSelection ? deriveStatusFromReadiness(this.selectedShortageReadiness) : 'info';
  }

  get selectedShortageGap(): number {
    const zone = this.selectedShortageZone;
    if (!zone) {
      return 0;
    }
    return Math.max(zone.workersRequired - zone.workersCurrent, 0);
  }

  get attendanceRate(): number {
    const presentCount = this.workers.filter((worker) => worker.status === 'present').length;
    return Math.round((presentCount / Math.max(this.workers.length, 1)) * 100);
  }

  get attendanceTone(): FactoryStatus {
    if (this.attendanceRate >= 90) {
      return 'present';
    }
    if (this.attendanceRate >= 70) {
      return 'warning';
    }
    return 'absent';
  }

  get recommendationsForContext(): RecommendationCandidate[] {
    if (!this.selectedShortageZone) {
      return this.recommendations;
    }

    const zoneRecommendations = this.recommendations.filter(
      (recommendation) =>
        recommendation.targetLine === this.selectedShortageZone?.line &&
        recommendation.targetStage === this.selectedShortageZone?.name
    );

    const fallbackRecommendations = this.recommendations.filter((recommendation) => !zoneRecommendations.includes(recommendation));

    return [...zoneRecommendations, ...fallbackRecommendations];
  }

  get topRecommendation(): RecommendationCandidate | undefined {
    return this.recommendationsForContext[0];
  }

  get additionalRecommendations(): RecommendationCandidate[] {
    return this.recommendationsForContext.slice(1);
  }

  getZoneShortage(zone: SubStageDropZone): number {
    return Math.max(zone.workersRequired - zone.workersCurrent, 0);
  }

  getZoneReadiness(zone: SubStageDropZone): number {
    if (zone.workersRequired <= 0) {
      return 100;
    }
    return Math.round((zone.workersCurrent / zone.workersRequired) * 100);
  }

  getZoneReadinessTone(zone: SubStageDropZone): FactoryStatus {
    return deriveStatusFromReadiness(this.getZoneReadiness(zone));
  }

  private initializeDemoContext(): void {
    const contextLineName = this.route.snapshot.queryParamMap.get('lineName')?.trim();
    const contextStageName = this.route.snapshot.queryParamMap.get('stageName')?.trim();
    const lineId = this.route.snapshot.queryParamMap.get('lineId')?.trim() ?? '';
    const stageId = this.route.snapshot.queryParamMap.get('stageId')?.trim() ?? '';
    const source = this.route.snapshot.queryParamMap.get('source')?.trim() ?? 'manual';

    if (!contextLineName || !contextStageName) {
      return;
    }

    this.demoContext = {
      lineId,
      lineName: contextLineName,
      stageId,
      stageName: contextStageName,
      source: source === 'factory-map' ? 'factory-map' : 'manual'
    };
  }

  getZoneWorkers(zone: SubStageDropZone): AssignmentWorker[] {
    return this.workers.filter((worker) => zone.workerIds.includes(worker.id));
  }

  getZoneById(zoneId: string): SubStageDropZone | undefined {
    return this.subStageZones.find((zone) => zone.id === zoneId);
  }

  getWorkerById(workerId: string | null): AssignmentWorker | undefined {
    return this.workers.find((worker) => worker.id === workerId);
  }

  get availableWorkersForTemporary(): AssignmentWorker[] {
    return this.workers.filter((worker) => worker.assignmentType === 'غير محدد');
  }

  get replacementTargetWorker(): AssignmentWorker | undefined {
    return this.getWorkerById(this.replacementDialog.targetWorkerId);
  }

  get replacementSelectedWorker(): AssignmentWorker | undefined {
    return this.getWorkerById(this.replacementDialog.replacementWorkerId);
  }

  get replacementCandidates(): AssignmentWorker[] {
    const targetWorker = this.replacementTargetWorker;
    if (!targetWorker) {
      return [];
    }
    return this.workers.filter((worker) => worker.assignmentType === 'غير محدد' && worker.id !== targetWorker.id);
  }

  trackById(index: number, item: { id?: string }): string | number {
    return item.id ?? index;
  }

  trackByAssignment(index: number, item: AssignmentItem): string {
    return `${item.worker}-${item.from}-${item.to}-${index}`;
  }

  clearSimulationMessage(): void {
    this.lastSimulationMessage = '';
  }

  scoreTone(score: number): FactoryStatus {
    if (score >= 95) {
      return 'ready';
    }
    if (score >= 85) {
      return 'warning';
    }
    return 'critical';
  }

  openDefaultTemporary(): void {
    const defaultZone = this.selectedShortageZone ?? this.subStageZones[0];
    if (!defaultZone) {
      return;
    }
    this.openTemporaryDialog(defaultZone);
  }

  openTemporaryDialog(zone: SubStageDropZone): void {
    this.temporaryDialog = {
      isOpen: true,
      targetZoneId: zone.id,
      targetZoneLabel: `${zone.line} - ${zone.name}`,
      selectedWorkerId: this.availableWorkersForTemporary[0]?.id ?? null
    };
  }

  openTemporaryFromWorker(worker: AssignmentWorker): void {
    const targetZone = this.subStageZones[0];
    if (!targetZone) {
      return;
    }
    this.temporaryDialog = {
      isOpen: true,
      targetZoneId: targetZone.id,
      targetZoneLabel: `${targetZone.line} - ${targetZone.name}`,
      selectedWorkerId: worker.id
    };
  }

  closeTemporaryDialog(): void {
    this.temporaryDialog = {
      ...this.temporaryDialog,
      isOpen: false,
      selectedWorkerId: null
    };
  }

  selectTemporaryWorker(workerId: string): void {
    this.temporaryDialog.selectedWorkerId = workerId;
  }

  saveTemporaryAssignment(): void {
    const zone = this.getZoneById(this.temporaryDialog.targetZoneId);
    const worker = this.getWorkerById(this.temporaryDialog.selectedWorkerId);

    if (zone && worker) {
      this.lastSimulationMessage = `تمت محاكاة تعيين ${worker.fullName} في ${zone.line} - ${zone.name}.`;
    } else {
      this.lastSimulationMessage = 'لا يوجد عامل متاح في هذه المحاكاة حتى الآن.';
    }
    this.closeTemporaryDialog();
  }

  openReplacementDialog(worker: AssignmentWorker): void {
    this.replacementDialog = {
      isOpen: true,
      targetWorkerId: worker.id,
      replacementWorkerId: this.getReplacementCandidates(worker)[0]?.id ?? null
    };
  }

  openReplacementForZone(zone: SubStageDropZone): void {
    const zoneWorkers = this.getZoneWorkers(zone);
    if (zoneWorkers.length === 0) {
      return;
    }
    this.openReplacementDialog(zoneWorkers[0]);
  }

  getReplacementCandidates(worker: AssignmentWorker): AssignmentWorker[] {
    return this.workers.filter((item) => item.assignmentType === 'غير محدد' && item.id !== worker.id);
  }

  closeReplacementDialog(): void {
    this.replacementDialog = {
      ...this.replacementDialog,
      isOpen: false,
      replacementWorkerId: null
    };
  }

  selectReplacementWorker(workerId: string): void {
    this.replacementDialog.replacementWorkerId = workerId;
  }

  saveReplacement(): void {
    if (this.replacementTargetWorker && this.replacementSelectedWorker) {
      this.lastSimulationMessage = `تمت محاكاة استبدال ${this.replacementTargetWorker.fullName} بالعامل ${this.replacementSelectedWorker.fullName} (موضع الاختبار).`;
    } else {
      this.lastSimulationMessage = 'اختر مرشح استبدال قبل حفظ محاكاة الاستبدال.';
    }
    this.closeReplacementDialog();
  }
}
