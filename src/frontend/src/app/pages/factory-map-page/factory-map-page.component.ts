import { Component, OnDestroy, OnInit } from '@angular/core';
import { catchError, finalize, of, Subject, Subscription, takeUntil } from 'rxjs';
import { ManufacturingDataChanged } from '../../core/models/realtime-notification.models';
import { ManufacturingCommandCenterApiService } from '../../core/services/manufacturing-command-center-api.service';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import {
  CommandCenterFilters,
  ManufacturingCommandCenter,
  commandCenterOperationLabel,
  commandCenterReadinessLabel,
  commandCenterScopeMatches,
  defaultCommandCenterFilters
} from '../../shared/models/manufacturing-command-center.model';

@Component({
  selector: 'app-factory-map-page',
  templateUrl: './factory-map-page.component.html'
})
export class FactoryMapPageComponent implements OnInit, OnDestroy {
  filters = defaultCommandCenterFilters();
  data: ManufacturingCommandCenter | null = null;
  isLoading = true;
  isRefreshing = false;
  hasLoadError = false;
  readonly expandedFactories = new Set<string>();
  readonly expandedDepartments = new Set<string>();
  readonly expandedLines = new Set<string>();

  private readonly destroy$ = new Subject<void>();
  private stopRealtimeWatch?: () => void;
  private activeLoad?: Subscription;
  private loadVersion = 0;

  constructor(
    private readonly api: ManufacturingCommandCenterApiService,
    private readonly manufacturingRealtime: ManufacturingRealtimeService
  ) {}

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
    this.load();
  }

  retry(): void { this.load(); }
  operationLabel(status: string): string { return commandCenterOperationLabel(status); }
  readinessLabel(status: string): string { return commandCenterReadinessLabel(status); }
  statusClass(status: string): string { return `state-${status.replace(/([a-z])([A-Z])/g, '$1-$2').toLowerCase()}`; }
  ratioText(percentage: number | null): string { return percentage === null ? 'لا توجد بيانات' : `${percentage}%`; }
  isExpanded(kind: 'factory' | 'department' | 'line', id: string): boolean { return this.setFor(kind).has(id); }

  setExpanded(kind: 'factory' | 'department' | 'line', id: string, event: Event): void {
    const open = (event.currentTarget as HTMLDetailsElement).open;
    const target = this.setFor(kind);
    if (open) target.add(id); else target.delete(id);
  }

  trackById(_: number, item: { id?: string | null; productionOrderId?: string }): string {
    return item.id ?? item.productionOrderId ?? String(_);
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
      if (loadVersion !== this.loadVersion || !data) return;
      this.data = data;
      if (this.expandedFactories.size === 0) data.factories.forEach(factory => this.expandedFactories.add(factory.id));
    });
  }

  private setFor(kind: 'factory' | 'department' | 'line'): Set<string> {
    return kind === 'factory' ? this.expandedFactories : kind === 'department' ? this.expandedDepartments : this.expandedLines;
  }

  private matchesCurrentScope(change: ManufacturingDataChanged): boolean {
    return commandCenterScopeMatches(this.filters, change);
  }
}
