import { Injectable } from '@angular/core';
import { ConfirmationService } from 'primeng/api';
import { PlpActionKind, plpActionIconFor } from './product-action';

export interface PlpConfirmationRequest {
  readonly header: string;
  readonly message: string;
  readonly accept: () => void;
  readonly reject?: () => void;
  readonly key?: string;
  readonly acceptLabel?: string;
  readonly rejectLabel?: string;
  readonly acceptAction?: Extract<PlpActionKind, 'approve' | 'delete' | 'deactivate' | 'reject'>;
}

/** Centralized confirmation copy, actions, and PrimeNG service usage. */
@Injectable({ providedIn: 'root' })
export class PlpConfirmationService {
  constructor(private readonly confirmationService: ConfirmationService) {}

  confirm(request: PlpConfirmationRequest): void {
    const acceptAction = request.acceptAction ?? 'approve';
    this.confirmationService.confirm({
      key: request.key ?? 'plp-confirm',
      header: request.header,
      message: request.message,
      icon: plpActionIconFor(acceptAction),
      acceptLabel: request.acceptLabel ?? 'تأكيد',
      rejectLabel: request.rejectLabel ?? 'إلغاء',
      acceptButtonStyleClass: this.acceptButtonClass(acceptAction),
      rejectButtonStyleClass: 'p-button-text p-button-secondary',
      accept: request.accept,
      reject: request.reject
    });
  }

  private acceptButtonClass(action: PlpConfirmationRequest['acceptAction']): string {
    switch (action) {
      case 'delete':
      case 'reject':
        return 'p-button-danger';
      case 'deactivate':
        return 'p-button-warning';
      default:
        return 'p-button-success';
    }
  }
}
