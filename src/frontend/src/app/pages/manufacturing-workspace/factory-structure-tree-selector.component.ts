import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FactoryStructureEntityType, FactoryStructureTreeNode, filterFactoryStructureTree, findFactoryStructureNode } from './factory-structure-tree.adapter';

@Component({
  selector: 'app-factory-structure-tree-selector',
  templateUrl: './factory-structure-tree-selector.component.html',
  styleUrls: ['./factory-structure-tree-selector.component.scss']
})
export class FactoryStructureTreeSelectorComponent {
  @Input() nodes: FactoryStructureTreeNode[] = [];
  @Input() selectedNode: FactoryStructureTreeNode | null = null;
  @Input() label = 'نطاق بنية المصنع';
  @Input() placeholder = 'اختر من شجرة المصنع';
  @Input() emptyPathLabel = 'كل بنية المصنع';
  @Input() loading = false;
  @Input() disabled = false;
  @Input() showClear = true;
  @Input() selectableTypes: readonly FactoryStructureEntityType[] = ['line'];

  @Output() selectedNodeChange = new EventEmitter<FactoryStructureTreeNode | null>();
  @Output() nodeSelect = new EventEmitter<FactoryStructureTreeNode>();

  treeSearch = '';

  get visibleNodes(): FactoryStructureTreeNode[] { return filterFactoryStructureTree(this.nodes, this.treeSearch); }
  get selectedPath(): string { return this.selectedNode ? this.nodePath(this.selectedNode).join(' ← ') : this.emptyPathLabel; }

  selectNode(node: FactoryStructureTreeNode): void {
    const type = node.data?.entityType;
    if (!type || !this.selectableTypes.includes(type)) return;
    this.selectedNode = node;
    this.selectedNodeChange.emit(node);
    this.nodeSelect.emit(node);
  }

  clearSelection(event?: Event): void {
    event?.preventDefault();
    event?.stopPropagation();
    this.selectedNode = null;
    this.selectedNodeChange.emit(null);
  }

  clearSearch(): void { this.treeSearch = ''; }

  private nodePath(node: FactoryStructureTreeNode): string[] {
    const path = [node.data?.name ?? ''];
    let parentId = node.data?.parentId;
    while (parentId) {
      const parent = findFactoryStructureNode(this.nodes, parentId);
      if (!parent?.data) break;
      path.unshift(parent.data.name);
      parentId = parent.data.parentId;
    }
    return path.filter(Boolean);
  }
}
