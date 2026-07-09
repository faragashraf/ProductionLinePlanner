import { Component } from '@angular/core';

interface ProductionLine {
  name: string;
  status: 'جاهز' | 'تعطل جزئي' | 'تحت المراجعة';
}

@Component({
  selector: 'app-production-lines-page',
  templateUrl: './production-lines-page.component.html',
  styleUrls: ['./production-lines-page.component.scss']
})
export class ProductionLinesPageComponent {
  lines: ProductionLine[] = [
    { name: 'الخط الأحمر', status: 'جاهز' },
    { name: 'الخط الأزرق', status: 'تعطل جزئي' },
    { name: 'الخط الأخضر', status: 'تحت المراجعة' },
  ];
}
