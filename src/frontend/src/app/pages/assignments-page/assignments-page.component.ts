import { Component } from '@angular/core';

interface AssignmentItem {
  worker: string;
  from: string;
  to: string;
  type: 'ثابت' | 'مؤقت';
}

@Component({
  selector: 'app-assignments-page',
  templateUrl: './assignments-page.component.html',
  styleUrls: ['./assignments-page.component.scss']
})
export class AssignmentsPageComponent {
  assignments: AssignmentItem[] = [
    { worker: 'أحمد سعيد', from: 'خط أحمر - مرحلة الخلط', to: 'خط أحمر - مرحلة التغليف', type: 'ثابت' },
    { worker: 'سارة علي', from: 'غير محدد', to: 'خط أزرق - مرحلة التغذية', type: 'مؤقت' },
  ];
}
