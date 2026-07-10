import { Component, OnInit } from '@angular/core';
import { catchError, finalize, of } from 'rxjs';
import { MockDataService } from '../../core/services/mock-data.service';
import { WorkersApiData, WorkersApiService } from '../../core/services/workers-api.service';
import { WorkerPageItem } from '../../shared/models/worker.model';

@Component({
  selector: 'app-workers-page',
  templateUrl: './workers-page.component.html',
  styleUrls: ['./workers-page.component.scss']
})
export class WorkersPageComponent implements OnInit {
  isLoading = true;
  showFallbackWarning = false;
  isBackendDataIncomplete = false;
  fallbackWarningMessage: string | null = null;
  workers: WorkerPageItem[] = [];

  private readonly backendFailureWarning = 'لا يمكن الاتصال بالخادم حالياً، لذلك يتم عرض بيانات العمال التجريبية.';
  private readonly backendIncompleteWarning = 'لا توجد بيانات عمال مكتملة حالياً، لذلك يتم عرض بيانات تجريبية.';

  constructor(
    private readonly dataService: MockDataService,
    private readonly workersApiService: WorkersApiService
  ) {}

  ngOnInit(): void {
    this.workers = this.dataService.getWorkersMock();
    this.loadWorkers();
  }

  private loadWorkers(): void {
    this.workersApiService
      .loadWorkers()
      .pipe(
        catchError(() => {
          this.showFallbackWarning = true;
          this.isBackendDataIncomplete = false;
          this.fallbackWarningMessage = this.backendFailureWarning;
          return of(this.createFallbackWorkersPayload());
        }),
        finalize(() => {
          this.isLoading = false;
        })
      )
      .subscribe((payload) => {
        if (!payload.hasBackendData) {
          this.workers = this.dataService.getWorkersMock();
          this.showFallbackWarning = true;
          this.isBackendDataIncomplete = false;
          this.fallbackWarningMessage = this.fallbackWarningMessage ?? this.backendFailureWarning;
          return;
        }

        if (!payload.hasUsableBackendData) {
          this.workers = this.dataService.getWorkersMock();
          this.showFallbackWarning = true;
          this.isBackendDataIncomplete = true;
          this.fallbackWarningMessage = this.backendIncompleteWarning;
          return;
        }

        this.showFallbackWarning = false;
        this.isBackendDataIncomplete = false;
        this.fallbackWarningMessage = null;
        this.workers = payload.workers;
      });
  }

  private createFallbackWorkersPayload(): WorkersApiData {
    return {
      workers: this.dataService.getWorkersMock(),
      hasBackendData: false,
      hasUsableBackendData: false
    };
  }

  trackByCode(_index: number, worker: WorkerPageItem): string {
    return `${worker.code}-${worker.fullName}`;
  }
}
