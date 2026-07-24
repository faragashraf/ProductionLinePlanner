import { Component, OnDestroy, OnInit } from '@angular/core';
import { catchError, finalize, of, Subject, Subscription, takeUntil } from 'rxjs';
import { ManufacturingDataChanged, RealtimeConnectionStatus, realtimeConnectionStatusLabel } from '../../core/models/realtime-notification.models';
import { ManufacturingCommandCenterApiService } from '../../core/services/manufacturing-command-center-api.service';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import {
  CommandCenterFilters,
  CommandCenterLine,
  CommandCenterLineStatusDimension,
  CommandCenterOperation,
  CommandCenterProblemLine,
  CommandCenterQualityIssue,
  CommandCenterWorkerDetail,
  ManufacturingCommandCenter,
  commandCenterLineStatusDimensions,
  commandCenterOperationLabel,
  commandCenterProblemLines,
  commandCenterScopeMatches,
  defaultCommandCenterFilters
} from '../../shared/models/manufacturing-command-center.model';

type DashboardDetail = 'present' | 'present-assigned' | 'present-unassigned' | 'not-present' | 'drafts' | 'approved' | 'quality' | 'stage-shortage' | null;

@Component({
  selector: 'app-dashboard-page',
  templateUrl: './dashboard-page.component.html'
})
export class DashboardPageComponent implements OnInit, OnDestroy {
  filters = defaultCommandCenterFilters();
  data: ManufacturingCommandCenter | null = null;
  isLoading = true;
  isRefreshing = false;
  hasLoadError = false;
  dataIsCurrent = false;
  selectedDetail: DashboardDetail = null;
  readonly skeletons = [1, 2, 3, 4, 5, 6];

  private readonly destroy$ = new Subject<void>();
  private stopRealtimeWatch?: () => void;
  private activeLoad?: Subscription;
  private loadVersion = 0;

  constructor(
    private readonly api: ManufacturingCommandCenterApiService,
    private readonly manufacturingRealtime: ManufacturingRealtimeService
  ) {}

  get realtimeStatus$() { return this.manufacturingRealtime.connectionStatus$; }

  ngOnInit(): void {
    this.load();
    this.stopRealtimeWatch = this.manufacturingRealtime.watchScreen({
      screen: 'manufacturing-command-center',
      matches: change => this.matchesCurrentScope(change),
      refresh: () => this.load(true)
    });
  }

  ngOnDestroy(): void {
    this.stopRealtimeWatch?.();
    this.activeLoad?.unsubscribe();
    this.destroy$.next();
    this.destroy$.complete();
  }

  onFiltersChange(filters: CommandCenterFilters): void {
    this.filters = filters;
    this.selectedDetail = null;
    this.dataIsCurrent = false;
    this.load();
  }

  retry(): void { this.load(); }
  selectDetail(detail: DashboardDetail): void { this.selectedDetail = this.selectedDetail === detail ? null : detail; }
  operationLabel(status: string): string { return commandCenterOperationLabel(status); }
  realtimeLabel(status: RealtimeConnectionStatus): string { return realtimeConnectionStatusLabel(status); }
  realtimeClass(status: RealtimeConnectionStatus): string { return `realtime-status--${status}`; }
  lineDimensions(line: CommandCenterLine): CommandCenterLineStatusDimension[] { return commandCenterLineStatusDimensions(line); }
  dimensionClass(dimension: CommandCenterLineStatusDimension): string { return `line-dimension--${dimension.tone}`; }
  ratioText(percentage: number | null): string { return percentage === null ? 'لا توجد بيانات' : `${percentage}%`; }

  get problemLines(): CommandCenterProblemLine[] {
    return this.data && this.dataIsCurrent ? commandCenterProblemLines(this.data) : [];
  }

  get detailTitle(): string {
    return ({
      present: 'الحاضرون في النطاق',
      'present-assigned': 'الحاضرون المسكنون دائمًا',
      'present-unassigned': 'الحاضرون غير المسكنين',
      'not-present': 'المسكنون الدائمون غير الحاضرين',
      drafts: 'المسودات التي تحتاج إجراء',
      approved: 'تشغيلات اليوم المعتمدة',
      quality: 'مشكلات اكتمال البيانات',
      'stage-shortage': 'المراحل المطلوبة بلا عامل حاضر'
    } as Record<string, string>)[this.selectedDetail ?? ''] ?? '';
  }

  get detailWorkers(): CommandCenterWorkerDetail[] {
    if (!this.data) return [];
    if (this.selectedDetail === 'present') {
      return [...this.data.workforce.presentAssignedDetails, ...this.data.workforce.presentUnassignedDetails]
        .filter((worker, index, workers) => workers.findIndex(candidate => candidate.workerId === worker.workerId) === index);
    }
    if (this.selectedDetail === 'present-assigned') return this.data.workforce.presentAssignedDetails;
    if (this.selectedDetail === 'present-unassigned') return this.data.workforce.presentUnassignedDetails;
    if (this.selectedDetail === 'not-present') return this.data.workforce.assignedNotPresentDetails;
    return [];
  }

  get detailOperations(): CommandCenterOperation[] {
    if (!this.data) return [];
    if (this.selectedDetail === 'drafts') return this.data.operations.items.filter(item => item.status === 'Draft' || item.status === 'ApprovalCancelled');
    if (this.selectedDetail === 'approved') return this.data.operations.items.filter(item => item.status === 'Approved');
    return [];
  }

  get detailIssues(): CommandCenterQualityIssue[] {
    if (!this.data) return [];
    if (this.selectedDetail === 'stage-shortage') return this.data.dataQuality.issues.filter(issue => issue.type === 'StageWithoutPresentWorker');
    if (this.selectedDetail === 'quality') return this.data.dataQuality.issues;
    return [];
  }

  private load(background = false): void {
    const loadVersion = ++this.loadVersion;
    this.activeLoad?.unsubscribe();
    if (background && this.data) this.isRefreshing = true;
    else this.isLoading = true;
    this.hasLoadError = false;
    this.activeLoad = this.api.load(this.filters).pipe(
      catchError(() => {
        if (loadVersion === this.loadVersion) this.hasLoadError = true;
        return of(null);
      }),
      finalize(() => {
        if (loadVersion !== this.loadVersion) return;
        this.isLoading = false;
        this.isRefreshing = false;
      }),
      takeUntil(this.destroy$)
    ).subscribe(data => {
      if (loadVersion === this.loadVersion && data) {
        this.data = data;
        this.dataIsCurrent = true;
      }
    });
  }

  private matchesCurrentScope(change: ManufacturingDataChanged): boolean {
    return commandCenterScopeMatches(this.filters, change);
  }
}
