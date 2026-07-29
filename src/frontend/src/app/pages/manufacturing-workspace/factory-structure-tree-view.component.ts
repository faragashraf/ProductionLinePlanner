import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FactoryStructureEntityType, FactoryStructureTreeNode } from './factory-structure-tree.adapter';

@Component({
  selector: 'app-factory-structure-tree-view',
  templateUrl: './factory-structure-tree-view.component.html',
  styleUrls: ['./factory-structure-tree-view.component.scss']
})
export class FactoryStructureTreeViewComponent {
  @Input() nodes: FactoryStructureTreeNode[] = [];
  @Input() selectedNode: FactoryStructureTreeNode | null = null;
  @Input() loading = false;
  @Input() actionDisabled = false;
  @Input() showActions = true;
  @Input() selectableTypes: readonly FactoryStructureEntityType[] = ['factory', 'department', 'line'];

  @Output() selectedNodeChange = new EventEmitter<FactoryStructureTreeNode | null>();
  @Output() nodeSelect = new EventEmitter<FactoryStructureTreeNode>();
  @Output() nodeExpand = new EventEmitter<FactoryStructureTreeNode>();
  @Output() nodeCollapse = new EventEmitter<FactoryStructureTreeNode>();
  @Output() nodeAction = new EventEmitter<{ event: MouseEvent; node: FactoryStructureTreeNode }>();

  nodeIcon(type: FactoryStructureEntityType): string {
    return type === 'factory' ? 'pi pi-building' : type === 'department' ? 'pi pi-sitemap' : 'pi pi-cog';
  }

  onNodeSelect(event: { node: FactoryStructureTreeNode }): void {
    if (!this.isSelectable(event.node)) return;
    this.selectedNode = event.node;
    this.selectedNodeChange.emit(event.node);
    this.nodeSelect.emit(event.node);
  }

  onNodeExpand(event: { node: FactoryStructureTreeNode }): void {
    this.nodeExpand.emit(event.node);
  }

  onNodeCollapse(event: { node: FactoryStructureTreeNode }): void {
    this.nodeCollapse.emit(event.node);
  }

  openNodeAction(event: MouseEvent, node: FactoryStructureTreeNode): void {
    event.preventDefault();
    event.stopPropagation();
    this.nodeAction.emit({ event, node });
  }

  isSelectable(node: FactoryStructureTreeNode): boolean {
    const type = node.data?.entityType;
    return !!type && this.selectableTypes.includes(type);
  }

  selectableNodes(nodes: readonly FactoryStructureTreeNode[]): FactoryStructureTreeNode[] {
    nodes.forEach(node => {
      node.selectable = this.isSelectable(node);
      if (node.children?.length) this.selectableNodes(node.children as FactoryStructureTreeNode[]);
    });
    return nodes as FactoryStructureTreeNode[];
  }
}
