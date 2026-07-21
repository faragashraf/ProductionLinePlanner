import { TreeNode } from 'primeng/api';
import { DepartmentItem, FactoryItem, ProductionLineOption } from '../../core/services/manufacturing-master-data-api.service';
import { matchesSearchTerm } from '../../shared/utils/text-search.utils';

export type FactoryStructureEntityType = 'factory' | 'department' | 'line';
export interface FactoryStructureDeleteEligibility { canDelete: boolean; deleteBlockReason?: string | null; }
export interface FactoryStructureTreeNodeData extends FactoryStructureDeleteEligibility {
  entityId: string;
  entityType: FactoryStructureEntityType;
  parentId?: string;
  name: string;
  code?: string;
  isActive: boolean;
  source: FactoryItem | DepartmentItem | ProductionLineOption;
}
export type FactoryStructureTreeNode = TreeNode<FactoryStructureTreeNodeData>;
export interface FactoryStructureTreeData { factories: readonly FactoryItem[]; departments: readonly DepartmentItem[]; lines: readonly ProductionLineOption[]; eligibility: ReadonlyMap<string, FactoryStructureDeleteEligibility>; }

export function buildFactoryStructureTree(data: FactoryStructureTreeData, expandedIds: ReadonlySet<string> = new Set()): FactoryStructureTreeNode[] {
  const departmentsByFactory = groupBy(data.departments.filter(item => !!item.id && !!item.factoryId), item => item.factoryId!);
  const linesByDepartment = groupBy(data.lines.filter(item => !!item.departmentId), item => item.departmentId!);
  const directLinesByFactory = groupBy(data.lines.filter(item => !item.departmentId), item => item.factoryId);
  return [...data.factories].sort(sortByNameAndCode).map(factory => {
    const departments = (departmentsByFactory.get(factory.id) ?? []).sort(sortByNameAndCode).map(department => {
      const lines = (linesByDepartment.get(department.id!) ?? []).sort(sortByNameAndCode)
        .map(line => createNode('line', line.id, line.name, line.lineCode, line.isActive, line, department.id, undefined, expandedIds, data.eligibility));
      return createNode('department', department.id!, department.nameAr ?? department.name ?? department.code ?? 'قسم محلي', department.code, department.isActive !== false, department, factory.id, lines, expandedIds, data.eligibility);
    });
    const directLines = (directLinesByFactory.get(factory.id) ?? []).sort(sortByNameAndCode)
      .map(line => createNode('line', line.id, line.name, line.lineCode, line.isActive, line, factory.id, undefined, expandedIds, data.eligibility));
    return createNode('factory', factory.id, factory.name, factory.code, factory.isActive, factory, undefined, [...departments, ...directLines], expandedIds, data.eligibility);
  });
}

export function findFactoryStructureNode(nodes: readonly FactoryStructureTreeNode[], entityId: string): FactoryStructureTreeNode | undefined { for (const node of nodes) { if (node.data?.entityId === entityId) return node; const child = findFactoryStructureNode((node.children as FactoryStructureTreeNode[] | undefined) ?? [], entityId); if (child) return child; } return undefined; }
export function collectExpandedIds(nodes: readonly FactoryStructureTreeNode[]): Set<string> { const ids = new Set<string>(); const visit = (items: readonly FactoryStructureTreeNode[]) => items.forEach(node => { if (node.expanded && node.data?.entityId) ids.add(node.data.entityId); visit((node.children as FactoryStructureTreeNode[] | undefined) ?? []); }); visit(nodes); return ids; }
export function filterFactoryStructureTree(nodes: readonly FactoryStructureTreeNode[], search: string): FactoryStructureTreeNode[] { if (!search.trim()) return nodes as FactoryStructureTreeNode[]; const filter = (items: readonly FactoryStructureTreeNode[]): FactoryStructureTreeNode[] => items.reduce<FactoryStructureTreeNode[]>((result, node) => { const children = filter((node.children as FactoryStructureTreeNode[] | undefined) ?? []); const matches = !!node.data && matchesSearchTerm(search, [node.data.name, node.data.code]); if (matches || children.length) result.push({ ...node, expanded: children.length > 0 || node.expanded, children }); return result; }, []); return filter(nodes); }

function createNode(entityType: FactoryStructureEntityType, entityId: string, name: string, code: string | undefined, isActive: boolean, source: FactoryStructureTreeNodeData['source'], parentId: string | undefined, children: FactoryStructureTreeNode[] | undefined, expandedIds: ReadonlySet<string>, eligibility: ReadonlyMap<string, FactoryStructureDeleteEligibility>): FactoryStructureTreeNode { const deletion = eligibility.get(entityId) ?? { canDelete: false }; return { key: `${entityType}:${entityId}`, label: name, expanded: expandedIds.has(entityId), leaf: entityType === 'line', children, data: { entityId, entityType, parentId, name, code, isActive, source, ...deletion } }; }
function groupBy<T>(items: readonly T[], key: (item: T) => string): Map<string, T[]> { return items.reduce((result, item) => { const value = key(item); result.set(value, [...(result.get(value) ?? []), item]); return result; }, new Map<string, T[]>()); }
function sortByNameAndCode<T extends { name?: string; nameAr?: string; code?: string; lineCode?: string }>(left: T, right: T): number { return (left.name ?? left.nameAr ?? left.code ?? left.lineCode ?? '').localeCompare(right.name ?? right.nameAr ?? right.code ?? right.lineCode ?? ''); }
