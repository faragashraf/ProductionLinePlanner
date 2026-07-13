import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class IamConfirmationService {
  confirm(message: string): boolean {
    return window.confirm(message);
  }
}
