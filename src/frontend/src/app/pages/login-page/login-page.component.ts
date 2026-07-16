import { Component } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { TimeoutError, finalize } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { PRODUCT_IDENTITY } from '../../core/config/product-identity.config';

@Component({
  selector: 'app-login-page',
  templateUrl: './login-page.component.html',
  styleUrls: ['./login-page.component.scss']
})
export class LoginPageComponent {
  readonly productIdentity = PRODUCT_IDENTITY;

  email = '';
  password = '';
  isLoading = false;
  errorMessage = '';
  warningMessage = '';
  submitted = false;
  isPasswordVisible = false;

  constructor(
    private readonly router: Router,
    private readonly authService: AuthService
  ) {}

  onLogin(): void {
    if (this.isLoading) {
      return;
    }

    this.submitted = true;
    this.errorMessage = '';
    this.warningMessage = '';

    if (!this.email.trim() || !this.password) {
      this.errorMessage = 'يرجى إدخال البريد الإلكتروني وكلمة المرور.';
      return;
    }

    this.isLoading = true;
    this.authService.login(this.email, this.password)
      .pipe(finalize(() => {
        this.isLoading = false;
      }))
      .subscribe({
        next: () => {
          this.warningMessage = 'تم تسجيل الدخول بنجاح.';
          this.router.navigateByUrl('/dashboard');
        },
        error: error => {
          this.errorMessage = this.resolveLoginErrorMessage(error);
        }
      });
  }

  onEmailChanged(value: string): void {
    this.email = value;
    this.clearValidationError();
  }

  onPasswordChanged(value: string): void {
    this.password = value;
    this.clearValidationError();
  }

  togglePasswordVisibility(): void {
    this.isPasswordVisible = !this.isPasswordVisible;
  }

  get emailValidationMessage(): string {
    return this.submitted && !this.email.trim() ? 'البريد الإلكتروني مطلوب.' : '';
  }

  get passwordValidationMessage(): string {
    return this.submitted && !this.password ? 'كلمة المرور مطلوبة.' : '';
  }

  private resolveLoginErrorMessage(error: unknown): string {
    if (error instanceof TimeoutError || (error instanceof Error && error.name === 'TimeoutError')) {
      return 'انتهت مهلة الاتصال بالخادم. تأكد من تشغيل الواجهة الخلفية ثم حاول مرة أخرى.';
    }

    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) {
        return 'تعذر الاتصال بالخادم. تأكد من تشغيل الواجهة الخلفية ثم حاول مرة أخرى.';
      }

      if (error.status === 401) {
        return 'بيانات الدخول غير صحيحة. تحقق من البريد الإلكتروني وكلمة المرور.';
      }

      if (error.status === 400) {
        return 'يرجى إدخال بيانات دخول صحيحة.';
      }
    }

    return 'حدث خطأ غير متوقع أثناء تسجيل الدخول. حاول مرة أخرى.';
  }

  private clearValidationError(): void {
    if (this.submitted && this.email.trim() && this.password) {
      this.errorMessage = '';
    }
  }
}
