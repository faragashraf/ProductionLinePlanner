import { FactoryStatus } from './factory-status.model';

export type LayoutNodeType = 'factory' | 'line' | 'main-stage' | 'sub-stage' | 'worker';

export interface LayoutPosition {
  row?: number;
  column?: number;
  x?: number;
  y?: number;
  width?: number;
  height?: number;
}

export interface LayoutNode {
  id: string;
  name: string;
  type: LayoutNodeType;
  status?: FactoryStatus | string;
  readinessPercent?: number;
  workersCurrent?: number;
  workersRequired?: number;
  workerRequirementDefined?: boolean;
  staffingSummaryAvailable?: boolean;
  attendanceSummaryAvailable?: boolean;
  presentAssignedWorkers?: number;
  absentAssignedWorkers?: number;
  attendanceStatus?: string;
  attendanceSummaryText?: string;
  assignmentParticipationsCount?: number;
  position?: LayoutPosition;
  description?: string;
}

export interface FactoryLayout extends LayoutNode {
  type: 'factory';
  lines: ProductionLineLayout[];
}

export interface ProductionLineLayout extends LayoutNode {
  type: 'line';
  departmentId?: string | null;
  departmentName?: string | null;
  statusText: string;
  activeStageId: string;
  activeStageName: string;
  stages: MainStageLayout[];
}

export interface MainStageLayout extends LayoutNode {
  type: 'main-stage';
  note?: string;
  subStages: SubStageLayout[];
}

export interface SubStageLayout extends LayoutNode {
  type: 'sub-stage';
  workers: WorkerLayout[];
}

export interface WorkerLayout {
  id: string;
  fullName: string;
  code: string;
  status: FactoryStatus | string;
  assignmentType: string;
  lastActivity: string;
}
