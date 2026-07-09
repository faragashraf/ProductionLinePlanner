import { Component } from '@angular/core';
import { FactoryMapLine, MockDataService } from '../../core/services/mock-data.service';

@Component({
  selector: 'app-factory-map-page',
  templateUrl: './factory-map-page.component.html',
  styleUrls: ['./factory-map-page.component.scss']
})
export class FactoryMapPageComponent {
  lines: FactoryMapLine[] = [];

  constructor(private readonly dataService: MockDataService) {
    this.lines = this.dataService.getFactoryMapData();
  }
}
