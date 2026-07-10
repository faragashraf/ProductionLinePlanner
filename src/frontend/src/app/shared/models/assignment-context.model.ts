import { MainStageLayout, ProductionLineLayout, SubStageLayout } from './factory-visualization.model';

export type AssignmentContextSource = 'factory-map' | 'manual';

export interface AssignmentContext {
  source: AssignmentContextSource;
  productionLineName: string;
  mainStageName: string;
  subStageName: string;
  subStageId: string | null;
  subStageCode: string | null;
  isDemoContext: boolean;
}

export type AssignmentContextQueryParams = Record<string, string>;

export function createFactoryMapAssignmentContext(
  productionLine: ProductionLineLayout,
  mainStage: MainStageLayout,
  subStage?: SubStageLayout
): AssignmentContext {
  const subStageId = subStage && isBackendGuid(subStage.id) ? subStage.id : null;

  return {
    source: 'factory-map',
    productionLineName: productionLine.name,
    mainStageName: mainStage.name,
    subStageName: subStage?.name ?? '',
    subStageId,
    subStageCode: null,
    isDemoContext: !subStageId
  };
}

export function assignmentContextToQueryParams(context: AssignmentContext): AssignmentContextQueryParams {
  const params: AssignmentContextQueryParams = {
    source: context.source,
    productionLineName: context.productionLineName,
    mainStageName: context.mainStageName,
    subStageName: context.subStageName,
    demo: String(context.isDemoContext)
  };

  if (context.subStageId) {
    params['subStageId'] = context.subStageId;
  }
  if (context.subStageCode) {
    params['subStageCode'] = context.subStageCode;
  }

  return params;
}

export function readAssignmentContext(readParam: (key: string) => string | null): AssignmentContext | null {
  const productionLineName = readParam('productionLineName')?.trim() || readParam('lineName')?.trim() || '';
  const mainStageName = readParam('mainStageName')?.trim() || readParam('stageName')?.trim() || '';
  const subStageName = readParam('subStageName')?.trim() || '';
  const source = readParam('source')?.trim() === 'factory-map' ? 'factory-map' : 'manual';
  const requestedSubStageId = readParam('subStageId')?.trim() || '';
  const subStageId = isBackendGuid(requestedSubStageId) ? requestedSubStageId : null;
  const subStageCode = readParam('subStageCode')?.trim() || null;
  const isDemoParam = readParam('demo')?.trim().toLowerCase();
  const isDemoContext = isDemoParam === 'true' || !subStageId;

  if (!productionLineName && !mainStageName && !subStageName) {
    return null;
  }

  return {
    source,
    productionLineName,
    mainStageName,
    subStageName,
    subStageId,
    subStageCode,
    isDemoContext
  };
}

export function isBackendGuid(value: string | null | undefined): boolean {
  return !!value && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
}
