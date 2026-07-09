import { Component } from '@angular/core';

interface WorkerItem {
  code: string;
  fullName: string;
  state: 'جاهز' | 'متأخر' | 'غائب';
}

@Component({
  selector: 'app-workers-page',
  templateUrl: './workers-page.component.html',
  styleUrls: ['./workers-page.component.scss']
})
export class WorkersPageComponent {
  workers: WorkerItem[] = [
    { code: 'W-101', fullName: 'أحمد سعيد', state: 'جاهز' },
    { code: 'W-102', fullName: 'سارة علي', state: 'متأخر' },
    { code: 'W-109', fullName: 'محمود يونس', state: 'غائب' },
  ];
}
