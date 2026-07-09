import { Component } from '@angular/core';
import { DashboardCard, MockDataService } from '../../core/services/mock-data.service';

@Component({
  selector: 'app-dashboard-page',
  templateUrl: './dashboard-page.component.html',
  styleUrls: ['./dashboard-page.component.scss']
})
export class DashboardPageComponent {
  cards: DashboardCard[] = this.dataService.getDashboardCards();

  constructor(private readonly dataService: MockDataService) {}

  getTrendLabel(trend: string): string {
    if (trend === 'up') {
      return 'ارتفع';
    }
    if (trend === 'down') {
      return 'انخفض';
    }
    return 'مستقر';
  }
}
