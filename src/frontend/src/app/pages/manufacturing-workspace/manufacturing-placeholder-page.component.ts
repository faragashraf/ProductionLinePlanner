import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ManufacturingWorkspaceItem } from '../../core/config/manufacturing-workspace.config';

@Component({
  selector: 'app-manufacturing-placeholder-page',
  templateUrl: './manufacturing-placeholder-page.component.html',
  styleUrls: ['./manufacturing-placeholder-page.component.scss']
})
export class ManufacturingPlaceholderPageComponent implements OnInit, OnDestroy {
  item!: ManufacturingWorkspaceItem;
  isLoading = true;
  private loadingTimer: ReturnType<typeof setTimeout> | null = null;

  constructor(private readonly route: ActivatedRoute) {}

  ngOnInit(): void {
    this.item = this.route.snapshot.data['workspaceItem'] as ManufacturingWorkspaceItem;
    this.loadingTimer = setTimeout(() => {
      this.isLoading = false;
    }, 300);
  }

  ngOnDestroy(): void {
    if (this.loadingTimer) {
      clearTimeout(this.loadingTimer);
    }
  }

  get isDashboard(): boolean {
    return this.item.id === 'manufacturing-dashboard';
  }
}
