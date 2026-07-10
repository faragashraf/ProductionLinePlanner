import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { catchError, map, Observable, of, timeout } from 'rxjs';
import { ApiResponse } from '../models/api-response.model';
import { buildApiUrl } from '../config/api.config';
import { deriveStatusFromReadiness } from '../../shared/models/factory-status.model';
import {
  FactoryLayout,
  LayoutPosition,
  MainStageLayout,
  ProductionLineLayout,
  SubStageLayout,
  WorkerLayout
} from '../../shared/models/factory-visualization.model';

type RawRecord = Record<string, unknown>;
type FactoryMapFallbackReason = 'incomplete' | 'connection';

const FACTORY_MAP_REQUEST_TIMEOUT_MS = 1500;

export interface FactoryMapApiData {
  layout: FactoryLayout;
  hasBackendData: boolean;
  hasUsableBackendData: boolean;
  fallbackReason?: FactoryMapFallbackReason;
}

interface FactoryReadinessSummary {
  overallReadiness: number;
  workersCurrent: number;
  workersRequired: number;
  totalLines: number;
}

@Injectable({
  providedIn: 'root'
})
export class FactoryMapApiService {
  constructor(private readonly http: HttpClient) {}

  loadFactoryMapData(): Observable<FactoryMapApiData> {
    return this.getFactories().pipe(
      timeout(FACTORY_MAP_REQUEST_TIMEOUT_MS),
      map((factories) => {
        const selectedFactory = factories[0] ?? {};
        const mapped = this.mapFactoryLayout(
          factories,
          this.getNestedProductionLines(selectedFactory),
          [],
          [],
          this.getEmptyFactoryReadiness(),
          []
        );
        const hasBackendData = this.hasBackendData(factories);
        const hasUsableBackendData = this.hasUsableBackendData(mapped, hasBackendData);

        if (!hasBackendData || !hasUsableBackendData) {
          return this.createFallbackData('incomplete', hasBackendData);
        }

        return {
          layout: mapped,
          hasBackendData: true,
          hasUsableBackendData: true
        };
      }),
      catchError(() => of(this.createFallbackData('connection')))
    );
  }

  private getFactories(): Observable<RawRecord[]> {
    return this.http
      .get<ApiResponse<unknown>>(buildApiUrl('/api/factories'))
      .pipe(
        map((response) => this.parseEntityList(this.extractPayload(response))),
        map((factories) => factories.map((item) => this.normalizeObject(item)))
      );
  }

  private hasBackendData(factories: RawRecord[]): boolean {
    return factories.length > 0;
  }

  private getNestedProductionLines(factory: RawRecord): RawRecord[] {
    return this.toArray(this.pickFirst(factory, ['productionLines', 'lines', 'items']))
      .map((line) => this.normalizeObject(line));
  }

  private createFallbackData(
    fallbackReason: FactoryMapFallbackReason,
    hasBackendData = false
  ): FactoryMapApiData {
    return {
      layout: this.getEmptyFactoryLayout(),
      hasBackendData,
      hasUsableBackendData: false,
      fallbackReason
    };
  }

  private getEmptyFactoryReadiness(): FactoryReadinessSummary {
    return {
      overallReadiness: 0,
      workersCurrent: 0,
      workersRequired: 0,
      totalLines: 0
    };
  }

  private hasUsableBackendData(layout: FactoryLayout, hasBackendData: boolean): boolean {
    if (!hasBackendData) {
      return false;
    }

    if (layout.lines.length === 0) {
      return false;
    }

    const totalStages = layout.lines.reduce((sum, line) => sum + (line.stages?.length ?? 0), 0);
    return totalStages > 0;
  }

  private mapFactoryLayout(
    factories: RawRecord[],
    productionLines: RawRecord[],
    mainStages: RawRecord[],
    subStages: RawRecord[],
    factoryReadiness: FactoryReadinessSummary,
    lineReadiness: RawRecord[]
  ): FactoryLayout {
    const selectedFactory = factories.length > 0 ? factories[0] : {};
    const mainStageMap = this.groupByParentId(mainStages, ['lineId', 'productionLineId', 'parentLineId']);
    const subStageByMainMap = this.groupByParentId(subStages, ['mainStageId', 'parentMainStageId', 'parentId']);
    const lineReadinessById = this.indexByKey(lineReadiness, ['lineId', 'productionLineId', 'id', '_id']);

    const mappedLines = this.buildProductionLines(
      productionLines,
      mainStageMap,
      subStageByMainMap,
      lineReadinessById
    );

    const lines = mappedLines.length > 0 ? mappedLines : this.buildLinesFromReadiness(lineReadinessById, mainStageMap, subStageByMainMap);
    const totalWorkersCurrent = lines.reduce((sum, line) => sum + (line.workersCurrent ?? 0), 0);
    const totalWorkersRequired = lines.reduce((sum, line) => sum + (line.workersRequired ?? 0), 0);
    const factoryReadinessPercent = this.toPercent(
      this.pickFirst(selectedFactory, ['readinessPercent', 'readiness', 'overallReadiness', 'readinessPercentile']),
      totalWorkersCurrent,
      totalWorkersRequired,
      this.toPercentFromReadinessObject(factoryReadiness)
    );

    return {
      id: this.resolveString(selectedFactory, ['id', 'factoryId', '_id']) || 'factory-01',
      type: 'factory',
      name: this.resolveString(selectedFactory, ['name', 'factoryName', 'title']) || 'مصنع الطموح',
      status: deriveStatusFromReadiness(factoryReadinessPercent),
      readinessPercent: factoryReadinessPercent,
      workersCurrent: this.toNumber(
        this.pickFirst(selectedFactory, ['workersCurrent', 'currentWorkers', 'activeWorkers', 'activeWorkersCount']),
        totalWorkersCurrent
      ),
      workersRequired: this.toNumber(
        this.pickFirst(selectedFactory, ['workersRequired', 'requiredWorkers', 'expectedWorkers']),
        totalWorkersRequired
      ),
      description: this.resolveString(
        selectedFactory,
        ['description', 'summary', 'meta']
      ) || 'خريطة مرئية تعتمد على ميتاداتا المصانع.',
      lines
    };
  }

  private buildLinesFromReadiness(
    lineReadinessById: Map<string, RawRecord>,
    mainStageMap: Map<string, RawRecord[]>,
    subStageByMainMap: Map<string, RawRecord[]>
  ): ProductionLineLayout[] {
    return Array.from(lineReadinessById.entries()).map(([lineId], index) => {
      const readinessRecord = lineReadinessById.get(lineId) ?? {};
      const lineName = this.resolveString(readinessRecord, ['lineName', 'name', 'title'], `الخط ${index + 1}`);

      const stages = this.buildMainStages(
        lineId,
        this.resolveString(readinessRecord, ['name', 'lineName', 'title']) || lineName,
        mainStageMap.get(lineId) ?? [],
        subStageByMainMap,
        readinessRecord
      );

      const stageWorkersCurrent = stages.reduce((sum, stage) => sum + (stage.workersCurrent ?? 0), 0);
      const stageWorkersRequired = stages.reduce((sum, stage) => sum + (stage.workersRequired ?? 0), 0);
      const workersCurrent = this.toNumber(
        this.pickFirst(readinessRecord, ['workersCurrent', 'currentWorkers', 'activeWorkers']),
        stageWorkersCurrent
      );
      const workersRequired = this.toNumber(
        this.pickFirst(readinessRecord, ['workersRequired', 'requiredWorkers', 'expectedWorkers']),
        stageWorkersRequired
      );
      const readinessPercent = this.toPercent(
        this.pickFirst(readinessRecord, ['readinessPercent', 'readiness', 'lineReadinessPercent', 'readinessRate']),
        workersCurrent,
        workersRequired
      );
      const activeStage = stages.length > 0 ? stages[0] : null;

      return {
        id: lineId,
        type: 'line',
        name: lineName,
        status: deriveStatusFromReadiness(readinessPercent),
        readinessPercent,
        statusText: this.toStatusText(readinessPercent),
        activeStageId: activeStage?.id ?? '',
        activeStageName: activeStage?.name ?? 'بدون مرحلة نشطة',
        workersCurrent,
        workersRequired,
        stages,
        description: `خط ${lineName}`
      };
    });
  }

  private buildProductionLines(
    productionLines: RawRecord[],
    mainStageMap: Map<string, RawRecord[]>,
    subStageByMainMap: Map<string, RawRecord[]>,
    lineReadinessById: Map<string, RawRecord>
  ): ProductionLineLayout[] {
    return productionLines.map((lineRecord, lineIndex) => {
      const lineId = this.resolveString(lineRecord, ['id', 'lineId', 'productionLineId', '_id'], `line-${lineIndex + 1}`);
      const lineName = this.resolveString(lineRecord, ['name', 'lineName', 'title'], `الخط ${lineIndex + 1}`);
      const stagesFromLine = this.toArray(this.pickFirst(lineRecord, ['stages', 'mainStages', 'items']));
      const mainStageRecords = this.mergeById(mainStageMap.get(lineId) ?? [], stagesFromLine, ['id', 'mainStageId', 'stageId', '_id']);
      const readinessRecord = lineReadinessById.get(lineId) ?? {};
      const stages = this.buildMainStages(lineId, lineName, mainStageRecords, subStageByMainMap, readinessRecord);

      const activeStage = stages.length > 0 ? stages[0] : null;
      const inlineActiveStageId = this.resolveString(lineRecord, ['activeStageId', 'currentStageId']);
      const inlineActiveStageName = this.resolveString(lineRecord, ['activeStageName', 'currentStageName']);
      const stageWorkersCurrent = stages.reduce((sum, stage) => sum + (stage.workersCurrent ?? 0), 0);
      const stageWorkersRequired = stages.reduce((sum, stage) => sum + (stage.workersRequired ?? 0), 0);
      const workersCurrent = this.toNumber(
        this.pickFirst(lineRecord, ['workersCurrent', 'currentWorkers', 'activeWorkers']),
        this.toNumber(this.pickFirst(readinessRecord, ['workersCurrent', 'currentWorkers', 'activeWorkers']), stageWorkersCurrent)
      );
      const workersRequired = this.toNumber(
        this.pickFirst(lineRecord, ['workersRequired', 'requiredWorkers', 'expectedWorkers']),
        this.toNumber(this.pickFirst(readinessRecord, ['workersRequired', 'requiredWorkers', 'expectedWorkers']), stageWorkersRequired)
      );
      const readinessPercent = this.toPercent(
        this.pickFirst(lineRecord, ['readinessPercent', 'readiness', 'lineReadinessPercent']),
        workersCurrent,
        workersRequired
      );
      const statusText = this.resolveString(
        lineRecord,
        ['statusText', 'readinessLabel', 'status'],
        this.toStatusText(readinessPercent)
      );

      return {
        id: lineId,
        type: 'line',
        name: lineName,
        status: deriveStatusFromReadiness(readinessPercent),
        readinessPercent,
        statusText,
        activeStageId: inlineActiveStageId || activeStage?.id || '',
        activeStageName: inlineActiveStageName || activeStage?.name || 'بدون مرحلة نشطة',
        workersCurrent,
        workersRequired,
        position: this.parsePosition(lineRecord),
        stages,
        description: this.resolveString(lineRecord, ['description', 'summary'])
      };
    });
  }

  private buildMainStages(
    lineId: string,
    lineName: string,
    stages: RawRecord[],
    subStageByMainMap: Map<string, RawRecord[]>,
    lineReadinessRecord: RawRecord
  ): MainStageLayout[] {
    return stages.map((stageRecord, stageIndex) => {
      const stageId = this.resolveString(stageRecord, ['id', 'mainStageId', 'stageId', '_id'], `${lineId}-stage-${stageIndex + 1}`);
      const stageName = this.resolveString(stageRecord, ['name', 'stageName', 'title'], `${lineName} - مرحلة`);

      const inlineSubStages = this.toArray(this.pickFirst(stageRecord, ['subStages', 'stages', 'items']));
      const subStagesRecords = this.mergeById(
        subStageByMainMap.get(stageId) ?? [],
        inlineSubStages,
        ['id', 'subStageId', 'workerStageId', '_id']
      );
      const workersFromSubStages = subStagesRecords.reduce<{ current: number; required: number }>(
        (acc, subStage) => {
          const subWorkers = this.parseWorkers(subStage);
          return {
            current: acc.current + subWorkers.currentCount,
            required: acc.required + subWorkers.requiredCount
          };
        },
        { current: 0, required: 0 }
      );
      const workersCurrent = this.toNumber(
        this.pickFirst(stageRecord, ['workersCurrent', 'currentWorkers', 'activeWorkers']),
        workersFromSubStages.current
      );
      const workersRequired = this.toNumber(
        this.pickFirst(stageRecord, ['workersRequired', 'requiredWorkers', 'expectedWorkers']),
        workersFromSubStages.required
      );
      const lineFallbackCurrent = this.toNumber(this.pickFirst(lineReadinessRecord, ['workersCurrent', 'currentWorkers']));
      const lineFallbackRequired = this.toNumber(this.pickFirst(lineReadinessRecord, ['workersRequired', 'requiredWorkers']));
      const readinessPercent = this.toPercent(
        this.pickFirst(stageRecord, ['readinessPercent', 'readiness']),
        workersCurrent || lineFallbackCurrent,
        workersRequired || lineFallbackRequired
      );
      const parsedSubStages = subStagesRecords.map((subStageRecord, subStageIndex) =>
        this.mapSubStage(subStageRecord, `${stageId}-sub-${subStageIndex + 1}`)
      );

      return {
        id: stageId,
        type: 'main-stage',
        name: stageName,
        status: deriveStatusFromReadiness(readinessPercent),
        readinessPercent,
        workersCurrent,
        workersRequired,
        note: this.resolveString(stageRecord, ['note', 'description']),
        position: this.parsePosition(stageRecord),
        subStages: parsedSubStages
      };
    });
  }

  private mapSubStage(subStageRecord: RawRecord, fallbackId: string): SubStageLayout {
    const subStageId = this.resolveString(subStageRecord, ['id', 'subStageId', '_id'], fallbackId);
    const subStageName = this.resolveString(subStageRecord, ['name', 'stageName', 'title'], 'مرحلة فرعية');
    const workers = this.parseWorkers(subStageRecord).workers;
    const workersCurrent = this.toNumber(
      this.pickFirst(subStageRecord, ['workersCurrent', 'currentWorkers']),
      workers.length
    );
    const workersRequired = this.toNumber(
      this.pickFirst(subStageRecord, ['workersRequired', 'requiredWorkers', 'expectedWorkers']),
      workersCurrent
    );
    const readinessPercent = this.toPercent(
      this.pickFirst(subStageRecord, ['readinessPercent', 'readiness']),
      workersCurrent,
      workersRequired
    );

    return {
      id: subStageId,
      type: 'sub-stage',
      name: subStageName,
      status: deriveStatusFromReadiness(readinessPercent),
      readinessPercent,
      workersCurrent,
      workersRequired,
      workers,
      position: this.parsePosition(subStageRecord)
    };
  }

  private parseWorkers(record: RawRecord): { workers: WorkerLayout[]; currentCount: number; requiredCount: number } {
    const workers = this.toArray(this.pickFirst(record, ['workers', 'assignedWorkers', 'crew', 'members'])).map((worker, workerIndex) => {
      const workerRecord = this.normalizeObject(worker);
      return {
        id: this.resolveString(workerRecord, ['id', 'workerId', '_id'], `worker-${workerIndex + 1}`),
        fullName: this.resolveString(workerRecord, ['fullName', 'name', 'employeeName'], `عامل ${workerIndex + 1}`),
        code: this.resolveString(workerRecord, ['code', 'workerCode', 'badge', 'employeeCode'], `W-${workerIndex + 1}`),
        status: this.resolveString(workerRecord, ['status', 'workerStatus', 'state'], 'info'),
        assignmentType: this.resolveString(workerRecord, ['assignmentType', 'type', 'employmentType'], 'غير محدد'),
        lastActivity: this.resolveString(
          workerRecord,
          ['lastActivity', 'lastActivityText', 'statusText', 'lastSeen'],
          'غير متاح'
        )
      };
    });

    const workersCurrent = this.toNumber(this.pickFirst(record, ['workersCurrent', 'currentWorkers']), workers.length);
    const workersRequired = this.toNumber(this.pickFirst(record, ['workersRequired', 'requiredWorkers', 'expectedWorkers']), workers.length);

    return {
      workers,
      currentCount: workersCurrent,
      requiredCount: workersRequired
    };
  }

  private parseFactoryReadiness(raw: unknown): FactoryReadinessSummary {
    const source = this.normalizeObject(raw);
    return {
      overallReadiness: this.toNumber(this.pickFirst(source, ['overallReadiness', 'readiness', 'overallReadyPercent'])),
      workersCurrent: this.toNumber(this.pickFirst(source, ['workersCurrent', 'currentWorkers', 'activeWorkers'])),
      workersRequired: this.toNumber(this.pickFirst(source, ['workersRequired', 'requiredWorkers', 'expectedWorkers'])),
      totalLines: this.toNumber(this.pickFirst(source, ['totalLines', 'linesCount', 'lineCount']))
    };
  }

  private toPercentFromReadinessObject(readiness: FactoryReadinessSummary): number {
    if (!readiness.overallReadiness) {
      return 0;
    }
    return this.clampPercent(Math.round(readiness.overallReadiness));
  }

  private parseEntityList(raw: unknown): unknown[] {
    const source = this.normalizeObject(raw);
    const candidates = this.pickFirst(source, ['factories', 'productionLines', 'mainStages', 'subStages', 'lines', 'items', 'data', 'results']);
    const candidateArray = this.toArray(candidates);
    if (candidateArray.length > 0) {
      return candidateArray;
    }

    if (raw === null || raw === undefined) {
      return [];
    }
    return Array.isArray(raw) ? raw : [];
  }

  private groupByParentId(records: RawRecord[], parentKeys: string[]): Map<string, RawRecord[]> {
    const grouped = new Map<string, RawRecord[]>();

    records.forEach((record) => {
      const parentId = this.resolveString(record, parentKeys);
      const bucket = parentId || '__unknown__';
      const collection = grouped.get(bucket) ?? [];
      collection.push(record);
      grouped.set(bucket, collection);
    });

    return grouped;
  }

  private indexByKey(records: RawRecord[], keys: string[]): Map<string, RawRecord> {
    const map = new Map<string, RawRecord>();
    records.forEach((record) => {
      const key = this.resolveString(record, keys);
      if (key) {
        map.set(key, record);
      }
    });
    return map;
  }

  private mergeById(
    primary: RawRecord[],
    secondary: unknown[],
    keys: string[]
  ): RawRecord[] {
    const map = new Map<string, RawRecord>();

    primary.forEach((item, index) => {
      const key = this.resolveString(item, keys, `__item-${index}`);
      map.set(key, item);
    });

    secondary.forEach((item, index) => {
      const record = this.normalizeObject(item);
      const key = this.resolveString(record, keys, `__item-${index}`);
      if (!map.has(key)) {
        map.set(key, record);
      }
    });

    return Array.from(map.values());
  }

  private parsePosition(record: RawRecord): LayoutPosition {
    return {
      row: this.toNumber(this.pickFirst(record, ['row', 'y'])),
      column: this.toNumber(this.pickFirst(record, ['column', 'x'])),
      x: this.toNumber(this.pickFirst(record, ['positionX', 'layoutX'])),
      y: this.toNumber(this.pickFirst(record, ['positionY', 'layoutY'])),
      width: this.toNumber(this.pickFirst(record, ['width', 'layoutWidth'])),
      height: this.toNumber(this.pickFirst(record, ['height', 'layoutHeight']))
    };
  }

  private resolveString(record: RawRecord, keys: string[], fallback = ''): string {
    const value = this.pickFirst(record, keys);
    return typeof value === 'string' && value.trim().length > 0 ? value.trim() : fallback;
  }

  private toPercent(
    rawPercent: unknown,
    currentWorkers: number,
    requiredWorkers: number,
    fallbackPercent?: number
  ): number {
    const percent = this.toNumber(rawPercent);
    if (percent > 0) {
      return this.clampPercent(percent);
    }
    if (requiredWorkers > 0) {
      return this.toPercentFromWorkers(currentWorkers, requiredWorkers);
    }
    return this.clampPercent(this.toNumber(fallbackPercent));
  }

  private toPercentFromWorkers(currentWorkers: number, requiredWorkers: number): number {
    return requiredWorkers > 0 ? this.clampPercent(Math.round((currentWorkers / requiredWorkers) * 100)) : 0;
  }

  private toStatusText(percent: number): string {
    const status = deriveStatusFromReadiness(percent);
    return status === 'ready' ? 'ممتاز' : status === 'warning' ? 'متوسط' : 'ضعيف';
  }

  private getEmptyFactoryLayout(): FactoryLayout {
    return {
      id: 'factory-empty',
      type: 'factory',
      name: 'مصنع الطموح',
      status: 'critical',
      readinessPercent: 0,
      workersCurrent: 0,
      workersRequired: 0,
      description: 'لا توجد بيانات تشغيل متاحة من الخادم.',
      lines: []
    };
  }

  private toNumber(value: unknown, fallback = 0): number {
    if (typeof value === 'number' && Number.isFinite(value)) {
      return value;
    }
    if (typeof value === 'string' && value.trim().length > 0) {
      const parsed = Number(value);
      if (Number.isFinite(parsed)) {
        return parsed;
      }
    }
    if (typeof value === 'boolean') {
      return value ? 1 : 0;
    }
    return fallback;
  }

  private toArray(value: unknown): unknown[] {
    return Array.isArray(value) ? value : [];
  }

  private pickFirst(record: Record<string, unknown>, keys: string[]): unknown {
    for (const key of keys) {
      if (Object.prototype.hasOwnProperty.call(record, key)) {
        const value = record[key];
        if (value !== undefined && value !== null) {
          return value;
        }
      }
    }
    return undefined;
  }

  private extractPayload<T>(response: ApiResponse<T>): T {
    if (response && typeof response === 'object' && 'success' in response) {
      if (response.success === false) {
        throw new Error(response.error?.message || 'API returned an unsuccessful response.');
      }
      if (!response.data) {
        throw new Error('API response data is missing.');
      }
      return response.data;
    }

    return response as T;
  }

  private normalizeObject(value: unknown): RawRecord {
    return value && typeof value === 'object' && !Array.isArray(value) ? value as RawRecord : {};
  }

  private clampPercent(value: number): number {
    if (value < 0) {
      return 0;
    }
    if (value > 100) {
      return 100;
    }
    return value;
  }
}
