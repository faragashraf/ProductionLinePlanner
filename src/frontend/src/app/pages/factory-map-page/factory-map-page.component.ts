import { ChangeDetectionStrategy, Component, OnDestroy, OnInit } from '@angular/core';
import { Subject, takeUntil } from 'rxjs';
import { RealtimeConnectionStatus, realtimeConnectionStatusLabel } from '../../core/models/realtime-notification.models';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import { FactoryReadinessStore } from './factory-readiness.store';

@Component({
  selector: 'app-factory-map-page',
  templateUrl: './factory-map-page.component.html',
  styleUrls: ['./factory-map-page.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [FactoryReadinessStore]
})
export class FactoryMapPageComponent implements OnInit, OnDestroy {
  readonly realtimeStatus$ = this.realtime.connectionStatus$;
  private readonly destroy$ = new Subject<void>();
  private stopRealtimeWatch?: () => void;

  constructor(
    readonly store: FactoryReadinessStore,
    private readonly realtime: ManufacturingRealtimeService
  ) {}

  ngOnInit(): void {
    this.store.loadSnapshot();
    this.realtime.connectionStatus$.pipe(takeUntil(this.destroy$)).subscribe(status => {
      this.store.realtimeDegraded.set(status !== 'connected');
    });
    this.stopRealtimeWatch = this.realtime.watchScreen({
      screen: 'factory-readiness',
      coalesceMs: 0,
      refresh: change => {
        if (change?.operationalReadiness) this.store.applyDelta(change.operationalReadiness);
        else this.store.loadSnapshot(true);
      }
    });
  }

  ngOnDestroy(): void {
    this.stopRealtimeWatch?.();
    this.destroy$.next();
    this.destroy$.complete();
  }

  realtimeLabel(status: RealtimeConnectionStatus): string { return realtimeConnectionStatusLabel(status); }
  realtimeClass(status: RealtimeConnectionStatus): string { return `realtime-status--${status}`; }
}
