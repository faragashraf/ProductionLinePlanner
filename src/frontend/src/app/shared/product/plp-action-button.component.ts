import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { PlpActionKind, plpActionDefinitionFor, plpActionIconFor } from './product-action';

/** The canonical operational action button. */
@Component({
  selector: 'plp-action-button',
  standalone: true,
  imports: [CommonModule, ButtonModule],
  template: `
    <button
      pButton
      type="button"
      [label]="label || definition.labelAr"
      [icon]="icon || iconName"
      [ngClass]="buttonClass"
      [disabled]="disabled || loading"
      [attr.aria-busy]="loading || null"
      (click)="trigger()"
    ></button>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpActionButtonComponent {
  @Input() action: PlpActionKind = 'save';
  @Input() label = '';
  @Input() icon = '';
  @Input() disabled = false;
  @Input() loading = false;
  @Input() iconOnly = false;
  @Output() triggered = new EventEmitter<void>();

  get definition() {
    return plpActionDefinitionFor(this.action);
  }

  get iconName(): string {
    return plpActionIconFor(this.action);
  }

  get buttonClass(): string {
    const classes = ['plp-action-button', `plp-action-button--${this.definition.tone}`];
    if (this.definition.outlined) {
      classes.push('p-button-outlined');
    }
    if (this.iconOnly) {
      classes.push('p-button-icon-only');
    }
    return classes.join(' ');
  }

  trigger(): void {
    if (!this.disabled && !this.loading) {
      this.triggered.emit();
    }
  }
}
