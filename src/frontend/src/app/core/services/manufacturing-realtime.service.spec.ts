import { BehaviorSubject, Subject } from 'rxjs';
import { ManufacturingDataChanged, RealtimeConnectionStatus } from '../models/realtime-notification.models';
import { ManufacturingRealtimeService } from './manufacturing-realtime.service';
import { RealtimeService } from './realtime.service';

describe('ManufacturingRealtimeService', () => {
  let realtime: FakeRealtime;
  let service: ManufacturingRealtimeService;

  beforeEach(() => {
    realtime = new FakeRealtime();
    service = new ManufacturingRealtimeService(realtime as unknown as RealtimeService);
  });

  afterEach(() => service.ngOnDestroy());

  it('exposes the actual shared SignalR connection status to manufacturing screens', () => {
    const statuses: RealtimeConnectionStatus[] = [];
    const subscription = service.connectionStatus$.subscribe(status => statuses.push(status));

    realtime.status.next('connecting');
    realtime.status.next('reconnecting');
    realtime.status.next('connected');

    expect(statuses).toEqual(['disconnected', 'connecting', 'reconnecting', 'connected']);
    subscription.unsubscribe();
  });

  it('uses one shared connection facade, joins once, coalesces events, and leaves after the final watcher', async () => {
    const first = jasmine.createSpy('first');
    const second = jasmine.createSpy('second');
    const stopFirst = service.watchScreen({ screen: 'models', refresh: first });
    const stopSecond = service.watchScreen({ screen: 'models', refresh: second });

    realtime.status.next('connected');
    await settle();
    expect(realtime.invocations).toEqual([['JoinManufacturingScreen', 'models']]);

    realtime.changes.next(change('event-1'));
    realtime.changes.next(change('event-1'));
    realtime.changes.next(change('event-2'));
    await waitForCoalescing();
    expect(first).toHaveBeenCalledTimes(1);
    expect(second).toHaveBeenCalledTimes(1);

    stopFirst();
    expect(realtime.invocations).toEqual([['JoinManufacturingScreen', 'models']]);
    stopSecond();
    await settle();
    expect(realtime.invocations.at(-1)).toEqual(['LeaveManufacturingScreen', 'models']);
  });

  it('refreshes only matching screens and rejoins plus refetches once after reconnect', async () => {
    const stages = jasmine.createSpy('stages');
    const models = jasmine.createSpy('models');
    service.watchScreen({ screen: 'stages', matches: item => item.productionLineId === 'line-1', refresh: stages });
    service.watchScreen({ screen: 'models', refresh: models });
    realtime.status.next('connected');
    await settle();

    realtime.changes.next(change('other-line', 'line-2'));
    await waitForCoalescing();
    expect(stages).not.toHaveBeenCalled();
    expect(models).toHaveBeenCalledTimes(1);

    realtime.reconnected.next();
    await waitForCoalescing();
    expect(realtime.invocations.filter(item => item[0] === 'JoinManufacturingScreen').length).toBe(4);
    expect(stages).toHaveBeenCalledTimes(1);
    expect(models).toHaveBeenCalledTimes(2);
  });

  it('ignores every local echo for one multi-entity operation and still refreshes for a concurrent remote change', async () => {
    const refresh = jasmine.createSpy('refresh');
    service.watchScreen({ screen: 'models', refresh });
    realtime.status.next('connected');
    await settle();
    const localCorrelation = service.registerLocalOperation('models');

    realtime.changes.next(change('local-main-stage-event', null, localCorrelation, 'MainStage'));
    realtime.changes.next(change('local-sub-stage-event', null, localCorrelation, 'SubStage'));
    realtime.changes.next(change('remote-event', null, 'another-browser-operation'));
    await waitForCoalescing();

    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it('registers a UUID v4 local correlation when randomUUID is unavailable in an HTTP/LAN browser', () => {
    withGlobalCrypto({
      getRandomValues: (bytes: Uint8Array) => {
        bytes.fill(0xaa);
        return bytes;
      }
    } as unknown as Crypto, () => {
      const correlationId = service.registerLocalOperation('models');

      expect(correlationId).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i);
    });
  });

  it('leaves a screen after an in-flight join completes with no remaining watchers', async () => {
    let resolveJoin!: (joined: boolean) => void;
    realtime.nextJoin = new Promise<boolean>(resolve => resolveJoin = resolve);
    const stop = service.watchScreen({ screen: 'models', refresh: jasmine.createSpy('refresh') });

    realtime.status.next('connected');
    await settle();
    stop();
    resolveJoin(true);
    await settle();

    const leaveCalls = realtime.invocations.filter(call => call[0] === 'LeaveManufacturingScreen');
    expect(leaveCalls.length).toBeGreaterThanOrEqual(1);
    expect(leaveCalls.at(-1)).toEqual(['LeaveManufacturingScreen', 'models']);
  });

  it('uses a dedicated screen group for daily-production invalidations', async () => {
    const refresh = jasmine.createSpy('refresh');
    service.watchScreen({ screen: 'daily-production-operations', refresh });

    realtime.status.next('connected');
    await settle();
    expect(realtime.invocations).toContain(['JoinManufacturingScreen', 'daily-production-operations']);

    realtime.changes.next({
      ...change('daily-order', 'line-1', null, 'ProductionOrder'),
      productionDate: '2026-07-16',
      productModelId: 'model-1'
    });
    await waitForCoalescing();

    expect(refresh).toHaveBeenCalledTimes(1);
  });

  function change(eventId: string, productionLineId: string | null = null, correlationId: string | null = null, entityType: ManufacturingDataChanged['entityType'] = 'ProductModel'): ManufacturingDataChanged {
    return {
      eventId, entityType, changeType: 'Updated', entityId: 'model-1', occurredAtUtc: new Date().toISOString(), actorUserId: null,
      correlationId, factoryId: null, departmentId: null, productionLineId, mainStageId: null, productModelId: 'model-1', subStageId: null, productionDate: null, workerId: null
    };
  }

  async function settle(): Promise<void> { await Promise.resolve(); await new Promise<void>(resolve => setTimeout(resolve, 0)); }
  async function waitForCoalescing(): Promise<void> { await new Promise<void>(resolve => setTimeout(resolve, 180)); }

  function withGlobalCrypto(cryptoApi: Crypto, action: () => void): void {
    const descriptor = Object.getOwnPropertyDescriptor(globalThis, 'crypto');
    Object.defineProperty(globalThis, 'crypto', { configurable: true, value: cryptoApi });
    try {
      action();
    } finally {
      if (descriptor) Object.defineProperty(globalThis, 'crypto', descriptor);
      else Reflect.deleteProperty(globalThis, 'crypto');
    }
  }
});

class FakeRealtime {
  readonly changes = new Subject<ManufacturingDataChanged>();
  readonly reconnected = new Subject<void>();
  readonly status = new BehaviorSubject<RealtimeConnectionStatus>('disconnected');
  readonly manufacturingDataChanged$ = this.changes.asObservable();
  readonly reconnected$ = this.reconnected.asObservable();
  readonly connectionStatus$ = this.status.asObservable();
  readonly invocations: unknown[][] = [];
  nextJoin?: Promise<boolean>;

  async invoke(methodName: string, ...args: unknown[]): Promise<boolean> {
    if (this.status.value !== 'connected') return false;
    this.invocations.push([methodName, ...args]);
    if (methodName === 'JoinManufacturingScreen' && this.nextJoin) {
      const join = this.nextJoin;
      this.nextJoin = undefined;
      return join;
    }
    return true;
  }
}
