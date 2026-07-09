import { Component } from '@angular/core';

interface StageItem {
  line: string;
  stage: string;
}

@Component({
  selector: 'app-stages-page',
  templateUrl: './stages-page.component.html',
  styleUrls: ['./stages-page.component.scss']
})
export class StagesPageComponent {
  stageList: StageItem[] = [
    { line: 'الخط الأحمر', stage: 'مرحلة الخلط' },
    { line: 'الخط الأحمر', stage: 'مرحلة التغليف' },
    { line: 'الخط الأزرق', stage: 'مرحلة التغذية' }
  ];
}
