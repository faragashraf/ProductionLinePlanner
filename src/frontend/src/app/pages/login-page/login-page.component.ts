import { Component } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-login-page',
  templateUrl: './login-page.component.html',
  styleUrls: ['./login-page.component.scss']
})
export class LoginPageComponent {
  email = '';
  password = '';
  isLoading = false;
  errorMessage = '';
  warningMessage = '';

  constructor(
    private readonly router: Router,
    private readonly authService: AuthService
  ) {}

  onLogin(event: Event): void {
    event.preventDefault();
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

  private resolveLoginErrorMessage(error: unknown): string {
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
}
