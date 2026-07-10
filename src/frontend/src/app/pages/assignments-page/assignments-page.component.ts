import { Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { catchError, finalize, of, switchMap } from 'rxjs';
import {
  ApiAssignmentType,
  AssignmentRecommendation,
  AssignmentsApiService,
  SubStageWorkersData
} from '../../core/services/assignments-api.service';
import {
  AssignmentContext,
  isBackendGuid,
  readAssignmentContext
} from '../../shared/models/assignment-context.model';
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
  workerCode: string | null;
  isDemo: boolean;
  score: number | null;
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
  demoContext: AssignmentContext | null = null;
  isLoading = true;
  showFallbackWarning = false;
  isBackendDataIncomplete = false;
  fallbackWarningMessage: string | null = null;
  isRecommendationsLoading = false;
  recommendationsFallbackMessage: string | null = null;
  isSavingTemporary = false;
  isSavingReplacement = false;
  isActionError = false;
  private recommendationRequestVersion = 0;

  private readonly backendFailureWarning = 'لا يمكن الاتصال بالخادم حالياً، لذلك يتم عرض بيانات الإسناد التجريبية.';
  private readonly backendIncompleteWarning = 'لا توجد بيانات إسناد مكتملة حالياً، لذلك يتم عرض بيانات تجريبية.';
  private readonly recommendationFailureWarning = 'لا يمكن تحميل توصيات الإسناد من الخادم حالياً، لذلك يتم عرض توصيات تجريبية.';
  private readonly recommendationIncompleteWarning = 'لا توجد توصيات مكتملة حالياً، لذلك يتم عرض توصيات تجريبية.';

  constructor(
    private readonly route: ActivatedRoute,
    private readonly assignmentsApiService: AssignmentsApiService
  ) {}

  ngOnInit(): void {
    this.initializeDemoContext();
    this.loadSelectedSubStageWorkers();
    this.loadRecommendationsForContext();
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
      workerCode: 'W-115',
      isDemo: true,
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
      workerCode: 'W-106',
      isDemo: true,
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
      workerCode: 'W-121',
      isDemo: true,
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

  private readonly mockAssignments = this.assignments.map((assignment) => ({ ...assignment }));
  private readonly mockWorkers = this.workers.map((worker) => ({ ...worker }));
  private readonly mockSubStageZones = this.subStageZones.map((zone) => ({ ...zone, workerIds: [...zone.workerIds] }));
  private readonly mockRecommendations = this.recommendations.map((recommendation) => ({
    ...recommendation,
    reasons: [...recommendation.reasons],
    risks: [...recommendation.risks]
  }));

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

  get isBackendAssignmentContext(): boolean {
    return isBackendGuid(this.demoContext?.subStageId);
  }

  get isDemoContext(): boolean {
    return !!this.demoContext && !this.isBackendAssignmentContext;
  }

  get selectedShortageZone(): SubStageDropZone | undefined {
    if (!this.demoContext) {
      return undefined;
    }
    const selectedStageName = this.demoContext.subStageName || this.demoContext.mainStageName;
    return (
      this.subStageZones.find(
        (zone) => zone.line === this.demoContext!.productionLineName && zone.name === selectedStageName
      ) ??
      this.subStageZones.find((zone) => zone.line === this.demoContext!.productionLineName)
    );
  }

  get selectedShortageLabel(): string {
    if (!this.demoContext) {
      return 'يرجى اختيار مرحلة لربط التوصية';
    }
    const stageName = this.demoContext.subStageName || this.demoContext.mainStageName;
    return [this.demoContext.productionLineName, stageName].filter((value) => value.length > 0).join(' - ');
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

  get recommendationStatusLabel(): string {
    if (this.isRecommendationsLoading) {
      return 'جارٍ التحميل';
    }
    if (this.isDemoContext || this.recommendationsFallbackMessage) {
      return 'توصية تجريبية';
    }
    return this.hasContextSelection ? 'توصية متاحة' : 'مرشح عام';
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
    this.demoContext = readAssignmentContext((key) => this.route.snapshot.queryParamMap.get(key));
  }

  private loadSelectedSubStageWorkers(): void {
    const subStageId = this.demoContext?.subStageId ?? '';
    if (!isBackendGuid(subStageId)) {
      this.isLoading = false;
      return;
    }

    this.isLoading = true;
    this.assignmentsApiService
      .getSubStageWorkers(subStageId)
      .pipe(
        catchError(() => of(this.createConnectionFallbackData(subStageId))),
        finalize(() => {
          this.isLoading = false;
        })
      )
      .subscribe((data) => {
        if (!data.hasBackendData || !data.hasUsableBackendData) {
          this.restoreMockAssignments();
          this.showReadFallback(data.hasBackendData ? 'incomplete' : 'connection');
          return;
        }

        this.applyBackendSubStageWorkers(data);
        this.showFallbackWarning = false;
        this.isBackendDataIncomplete = false;
        this.fallbackWarningMessage = null;
      });
  }

  private loadRecommendationsForContext(): void {
    const subStageId = this.demoContext?.subStageId ?? '';
    const requestVersion = ++this.recommendationRequestVersion;
    if (!isBackendGuid(subStageId)) {
      this.isRecommendationsLoading = false;
      this.recommendations = this.createMockRecommendations();
      this.recommendationsFallbackMessage = null;
      return;
    }

    this.isRecommendationsLoading = true;
    this.recommendations = [];
    this.recommendationsFallbackMessage = null;
    this.assignmentsApiService
      .getRecommendations(subStageId)
      .pipe(
        finalize(() => {
          if (this.isLatestRecommendationRequest(requestVersion)) {
            this.isRecommendationsLoading = false;
          }
        })
      )
      .subscribe({
        next: (recommendations) => {
          if (!this.isLatestRecommendationRequest(requestVersion)) {
            return;
          }
          if (recommendations.length === 0) {
            this.useDemoRecommendations(this.recommendationIncompleteWarning);
            return;
          }

          this.recommendations = recommendations.map((recommendation) => this.mapRecommendation(recommendation));
        },
        error: () => {
          if (this.isLatestRecommendationRequest(requestVersion)) {
            this.useDemoRecommendations(this.recommendationFailureWarning);
          }
        }
      });
  }

  private applyBackendSubStageWorkers(data: SubStageWorkersData): void {
    const zone = this.selectedShortageZone ?? this.subStageZones[0];
    this.workers = data.workers.map((worker) => this.mapBackendWorker(worker, zone));
    this.assignments = data.workers.map((worker) => this.mapBackendAssignment(worker, zone));

    if (!zone) {
      return;
    }

    zone.workerIds = data.workers.map((worker) => worker.id);
    zone.workersCurrent = data.workers.length;
    zone.status = deriveStatusFromReadiness(this.getZoneReadiness(zone));
  }

  private mapBackendWorker(
    worker: { id: string; fullName: string; code: string; assignmentType: ApiAssignmentType },
    zone: SubStageDropZone | undefined
  ): AssignmentWorker {
    const assignmentType = this.mapBackendAssignmentType(worker.assignmentType);
    return {
      id: worker.id,
      code: worker.code,
      fullName: worker.fullName,
      status: assignmentType === 'مؤقت' ? 'warning' : assignmentType === 'غير محدد' ? 'unassigned' : 'ready',
      assignmentType,
      line: zone?.line ?? 'غير محدد',
      subStage: zone?.name ?? 'غير محدد',
      lastActivity: assignmentType === 'مؤقت' ? 'تعيين مؤقت نشط' : 'تعيين حالي من الخادم'
    };
  }

  private mapBackendAssignment(
    worker: { fullName: string; assignmentType: ApiAssignmentType; fromSubStageId: string | null },
    zone: SubStageDropZone | undefined
  ): AssignmentItem {
    const isTemporary = worker.assignmentType === 'Temporary' || worker.assignmentType === 'Replacement';
    return {
      worker: worker.fullName,
      from: isTemporary && worker.fromSubStageId ? 'مرحلة سابقة مرتبطة' : 'تعيين ثابت',
      to: zone ? `${zone.line} - ${zone.name}` : this.selectedShortageLabel,
      type: isTemporary ? 'مؤقت' : 'ثابت',
      status: isTemporary ? 'warning' : 'ready'
    };
  }

  private mapBackendAssignmentType(type: ApiAssignmentType): AssignmentWorker['assignmentType'] {
    if (type === 'Temporary' || type === 'Replacement') {
      return 'مؤقت';
    }
    return 'ثابت';
  }

  private mapRecommendation(recommendation: AssignmentRecommendation): RecommendationCandidate {
    return {
      workerName: recommendation.workerName,
      workerCode: recommendation.workerCode ?? null,
      isDemo: false,
      score: recommendation.score === null ? null : Math.round(recommendation.score),
      from: 'غير محدد',
      to: this.selectedShortageLabel,
      reasons: recommendation.reasons.length > 0 ? recommendation.reasons : ['لا توجد أسباب تفصيلية متاحة حالياً.'],
      risks: recommendation.risks,
      targetLine: this.demoContext?.productionLineName ?? '',
      targetStage: this.demoContext?.subStageName || this.demoContext?.mainStageName || ''
    };
  }

  private createConnectionFallbackData(subStageId: string): SubStageWorkersData {
    return {
      subStageId,
      workers: [],
      hasBackendData: false,
      hasUsableBackendData: false
    };
  }

  private showReadFallback(reason: 'connection' | 'incomplete'): void {
    this.showFallbackWarning = true;
    this.isBackendDataIncomplete = reason === 'incomplete';
    this.fallbackWarningMessage = reason === 'connection'
      ? this.backendFailureWarning
      : this.backendIncompleteWarning;
  }

  private restoreMockAssignments(): void {
    this.assignments = this.mockAssignments.map((assignment) => ({ ...assignment }));
    this.workers = this.mockWorkers.map((worker) => ({ ...worker }));
    this.subStageZones = this.mockSubStageZones.map((zone) => ({ ...zone, workerIds: [...zone.workerIds] }));
  }

  private useDemoRecommendations(message: string): void {
    this.recommendations = this.createMockRecommendations();
    this.recommendationsFallbackMessage = message;
  }

  private isLatestRecommendationRequest(requestVersion: number): boolean {
    return requestVersion === this.recommendationRequestVersion;
  }

  private createMockRecommendations(): RecommendationCandidate[] {
    return this.mockRecommendations.map((recommendation) => ({
      ...recommendation,
      reasons: [...recommendation.reasons],
      risks: [...recommendation.risks]
    }));
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
    this.isActionError = false;
  }

  scoreTone(score: number | null): FactoryStatus {
    if (score === null) {
      return 'info';
    }
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

    if (!zone || !worker) {
      this.showActionError('لا يوجد عامل متاح للتعيين المؤقت حالياً.');
      return;
    }

    if (!this.isBackendAssignmentContext) {
      this.showActionSuccess(`تمت محاكاة تعيين ${worker.fullName} في ${zone.line} - ${zone.name}.`);
      this.closeTemporaryDialog();
      return;
    }

    const targetSubStageId = this.getBackendTargetSubStageId(zone);
    if (!targetSubStageId || !isBackendGuid(worker.id)) {
      this.showActionError('تعذر حفظ التعيين المؤقت لأن بيانات الإسناد الحالية غير مكتملة.');
      return;
    }

    this.isSavingTemporary = true;
    this.assignmentsApiService
      .getCurrentWorkerAssignment(worker.id)
      .pipe(
        switchMap((currentAssignment) => {
          if (!currentAssignment.effectiveSubStageId || currentAssignment.effectiveSubStageId === targetSubStageId) {
            throw new Error('A valid source sub-stage is required for a temporary assignment.');
          }

          const assignmentWindow = this.createTemporaryAssignmentWindow();
          return this.assignmentsApiService.createTemporaryAssignment({
            workerId: worker.id,
            fromSubStageId: currentAssignment.effectiveSubStageId,
            toSubStageId: targetSubStageId,
            startAtUtc: assignmentWindow.startAtUtc,
            endAtUtc: assignmentWindow.endAtUtc,
            reason: 'تعيين مؤقت من واجهة إدارة التعيينات'
          });
        }),
        finalize(() => {
          this.isSavingTemporary = false;
        })
      )
      .subscribe({
        next: () => {
          this.showActionSuccess(`تم حفظ التعيين المؤقت للعامل ${worker.fullName}.`);
          this.closeTemporaryDialog();
          this.loadSelectedSubStageWorkers();
          this.loadRecommendationsForContext();
        },
        error: () => {
          this.showActionError('تعذر حفظ التعيين المؤقت. يرجى مراجعة بيانات العامل والمحاولة مرة أخرى.');
        }
      });
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
    const targetWorker = this.replacementTargetWorker;
    const replacementWorker = this.replacementSelectedWorker;
    if (!targetWorker || !replacementWorker) {
      this.showActionError('اختر مرشح استبدال قبل حفظ التعديل.');
      return;
    }

    if (!this.isBackendAssignmentContext) {
      this.showActionSuccess(`تمت محاكاة استبدال ${targetWorker.fullName} بالعامل ${replacementWorker.fullName} (موضع الاختبار).`);
      this.closeReplacementDialog();
      return;
    }

    const targetSubStageId = this.getBackendTargetSubStageId();
    if (!targetSubStageId || !isBackendGuid(targetWorker.id) || !isBackendGuid(replacementWorker.id)) {
      this.showActionError('تعذر حفظ الاستبدال لأن بيانات الإسناد الحالية غير مكتملة.');
      return;
    }

    const assignmentWindow = this.createTemporaryAssignmentWindow();
    this.isSavingReplacement = true;
    this.assignmentsApiService
      .createReplacementAssignment({
        replacementWorkerId: replacementWorker.id,
        replacedWorkerId: targetWorker.id,
        subStageId: targetSubStageId,
        startAtUtc: assignmentWindow.startAtUtc,
        endAtUtc: assignmentWindow.endAtUtc,
        reason: 'استبدال مؤقت من واجهة إدارة التعيينات'
      })
      .pipe(
        finalize(() => {
          this.isSavingReplacement = false;
        })
      )
      .subscribe({
        next: () => {
          this.showActionSuccess(`تم حفظ استبدال ${targetWorker.fullName} بالعامل ${replacementWorker.fullName}.`);
          this.closeReplacementDialog();
          this.loadSelectedSubStageWorkers();
          this.loadRecommendationsForContext();
        },
        error: () => {
          this.showActionError('تعذر حفظ الاستبدال. يرجى مراجعة بيانات العاملين والمحاولة مرة أخرى.');
        }
      });
  }

  private getBackendTargetSubStageId(zone?: SubStageDropZone): string | null {
    const selectedZone = this.selectedShortageZone ?? this.subStageZones[0];
    if (zone && selectedZone && zone.id !== selectedZone.id) {
      return null;
    }

    const subStageId = this.demoContext?.subStageId ?? '';
    return isBackendGuid(subStageId) ? subStageId : null;
  }

  private createTemporaryAssignmentWindow(): { startAtUtc: string; endAtUtc: string } {
    const start = new Date();
    const end = new Date(start.getTime() + 8 * 60 * 60 * 1000);
    return {
      startAtUtc: start.toISOString(),
      endAtUtc: end.toISOString()
    };
  }

  private showActionSuccess(message: string): void {
    this.isActionError = false;
    this.lastSimulationMessage = message;
  }

  private showActionError(message: string): void {
    this.isActionError = true;
    this.lastSimulationMessage = message;
  }

}
