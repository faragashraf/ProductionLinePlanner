import { Injectable, OnDestroy } from '@angular/core';
import { Subject, Subscription, auditTime, filter } from 'rxjs';
import { ManufacturingDataChanged, RealtimeConnectionStatus } from '../models/realtime-notification.models';
import { RealtimeService } from './realtime.service';

export type ManufacturingRealtimeScreen = 'factory-structure' | 'departments' | 'stages' | 'models' | 'employees' | 'daily-production-operations';

export interface ManufacturingRealtimeWatch {
  screen: ManufacturingRealtimeScreen;
  refresh: () => void;
  matches?: (change: ManufacturingDataChanged) => boolean;
}

interface ActiveWatch extends ManufacturingRealtimeWatch {
  id: number;
  refreshes: Subject<void>;
  subscription: Subscription;
}

/** Central manufacturing facade over the application-wide SignalR connection. */
@Injectable({ providedIn: 'root' })
export class ManufacturingRealtimeService implements OnDestroy {
  private readonly watches = new Map<number, ActiveWatch>();
  private readonly joinedScreens = new Set<ManufacturingRealtimeScreen>();
  private readonly joiningScreens = new Map<ManufacturingRealtimeScreen, Promise<void>>();
  private readonly seenEventIds = new Set<string>();
  private readonly localCorrelations = new Map<string, ManufacturingRealtimeScreen>();
  private readonly subscriptions = new Subscription();
  private nextWatchId = 1;
  private isConnected = false;

  constructor(private readonly realtime: RealtimeService) {
    this.subscriptions.add(this.realtime.manufacturingDataChanged$.subscribe(change => this.handleChange(change)));
    this.subscriptions.add(this.realtime.reconnected$.subscribe(() => { void this.restoreAfterReconnect(); }));
    this.subscriptions.add(this.realtime.connectionStatus$
      .pipe(filter(status => status === 'connected' || status === 'disconnected'))
      .subscribe(status => this.handleConnectionStatus(status)));
  }

  watchScreen(watch: ManufacturingRealtimeWatch): () => void {
    const id = this.nextWatchId++;
    const refreshes = new Subject<void>();
    const subscription = refreshes.pipe(auditTime(150)).subscribe(() => watch.refresh());
    this.watches.set(id, { ...watch, id, refreshes, subscription });
    if (this.isConnected) void this.joinScreen(watch.screen);
    return () => this.stopWatching(id);
  }

  /** Registers a locally-originated mutation so only this browser connection can ignore every echoed event for that operation. */
  registerLocalOperation(screen: ManufacturingRealtimeScreen): string {
    const correlationId = crypto.randomUUID();
    this.localCorrelations.set(correlationId, screen);
    if (this.localCorrelations.size > 256) this.localCorrelations.delete(this.localCorrelations.keys().next().value!);
    return correlationId;
  }

  ngOnDestroy(): void {
    for (const id of [...this.watches.keys()]) this.stopWatching(id);
    this.subscriptions.unsubscribe();
    this.joinedScreens.clear();
    this.joiningScreens.clear();
    this.localCorrelations.clear();
  }

  private handleChange(change: ManufacturingDataChanged): void {
    if (!change?.eventId || this.seenEventIds.has(change.eventId)) return;
    this.seenEventIds.add(change.eventId);
    if (this.seenEventIds.size > 256) this.seenEventIds.delete(this.seenEventIds.values().next().value!);
    // A single database mutation can emit several entity invalidations (for
    // example the compatibility MainStage and its new SubStage). Keep the
    // correlation identity for the bounded lifetime of this service instead of
    // consuming it on the first event.
    const localScreen = change.correlationId ? this.localCorrelations.get(change.correlationId) : undefined;
    for (const watch of this.watches.values()) {
      if (watch.screen === localScreen) continue;
      if (!watch.matches || watch.matches(change)) watch.refreshes.next();
    }
  }

  private handleConnectionStatus(status: RealtimeConnectionStatus): void {
    if (status === 'disconnected') {
      this.isConnected = false;
      this.joinedScreens.clear();
      this.joiningScreens.clear();
      return;
    }
    this.isConnected = true;
    void Promise.resolve().then(() => this.joinActiveScreens());
  }

  private async restoreAfterReconnect(): Promise<void> {
    this.joinedScreens.clear();
    await this.joinActiveScreens();
    for (const watch of this.watches.values()) watch.refreshes.next();
  }

  private async joinActiveScreens(): Promise<void> {
    for (const screen of new Set([...this.watches.values()].map(watch => watch.screen))) await this.joinScreen(screen);
  }

  private async joinScreen(screen: ManufacturingRealtimeScreen): Promise<void> {
    if (this.joinedScreens.has(screen)) return;
    const pending = this.joiningScreens.get(screen);
    if (pending) return pending;
    const enrollment = this.realtime.invoke('JoinManufacturingScreen', screen)
      .then(async joined => {
        if (!joined) return;
        if (this.hasWatchers(screen)) {
          this.joinedScreens.add(screen);
          return;
        }
        // The final watcher left while the join was in flight. Do not retain
        // the server-side group membership for a screen with no consumers.
        await this.realtime.invoke('LeaveManufacturingScreen', screen).catch(() => undefined);
      })
      .catch(() => undefined)
      .finally(() => this.joiningScreens.delete(screen));
    this.joiningScreens.set(screen, enrollment);
    return enrollment;
  }

  private stopWatching(id: number): void {
    const watch = this.watches.get(id);
    if (!watch) return;
    watch.subscription.unsubscribe();
    watch.refreshes.complete();
    this.watches.delete(id);
    if ([...this.watches.values()].some(candidate => candidate.screen === watch.screen)) return;
    this.joinedScreens.delete(watch.screen);
    if (this.isConnected) void this.realtime.invoke('LeaveManufacturingScreen', watch.screen).catch(() => undefined);
  }

  private hasWatchers(screen: ManufacturingRealtimeScreen): boolean {
    return [...this.watches.values()].some(watch => watch.screen === screen);
  }
}
