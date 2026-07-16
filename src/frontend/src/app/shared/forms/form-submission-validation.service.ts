import { Injectable } from '@angular/core';
import { AbstractControl, FormGroup } from '@angular/forms';

export interface RequiredFieldRule {
  control: string;
  message: string;
  isMissing?: () => boolean;
}

export interface SubmissionValidationResult {
  valid: boolean;
  messages: string[];
  summary: string;
}

/**
 * One submission path for Arabic required-field feedback. It deliberately leaves
 * server validation authoritative while preventing known-invalid requests locally.
 */
@Injectable({ providedIn: 'root' })
export class FormSubmissionValidationService {
  missingMessages(form: FormGroup, required: readonly RequiredFieldRule[], extra: readonly string[] = []): string[] {
    const missing = required
      .filter(rule => rule.isMissing ? rule.isMissing() : form.get(rule.control)?.invalid)
      .map(rule => rule.message);
    return [...new Set([...missing, ...extra.filter(Boolean)])];
  }

  validate(
    form: FormGroup,
    required: readonly RequiredFieldRule[],
    extra: readonly string[] = [],
    focusRoot: ParentNode | null = null
  ): SubmissionValidationResult {
    form.markAllAsTouched();
    const messages = this.missingMessages(form, required, extra);
    const valid = form.valid && messages.length === 0;
    if (!valid) this.focusFirstInvalid(form, required, focusRoot);

    return {
      valid,
      messages,
      summary: messages.length ? messages.join(' • ') : ''
    };
  }

  serverMessage(error: unknown, fallback: string): string {
    const response = error as {
      message?: unknown;
      detail?: unknown;
      error?: { message?: unknown; detail?: unknown; error?: { message?: unknown } };
    };
    const message = response?.error?.error?.message ?? response?.error?.message ?? response?.error?.detail ?? response?.detail ?? response?.message;
    if (typeof message !== 'string' || !message.trim() || /^Http failure response/i.test(message)) return fallback;
    return message.trim();
  }

  private focusFirstInvalid(form: FormGroup, required: readonly RequiredFieldRule[], focusRoot: ParentNode | null): void {
    if (typeof document === 'undefined') return;
    const invalidRule = required.find(rule => rule.isMissing ? rule.isMissing() : form.get(rule.control)?.invalid);
    const controlName = invalidRule?.control.split('.')[0];
    if (!controlName) return;

    queueMicrotask(() => {
      const root = focusRoot ?? document;
      const element = root.querySelector<HTMLElement>(`[formControlName="${controlName}"]`);
      element?.focus();
    });
  }
}
