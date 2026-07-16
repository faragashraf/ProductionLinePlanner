import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Router } from '@angular/router';
import { Subject, TimeoutError, of, throwError } from 'rxjs';
import { ButtonModule } from 'primeng/button';
import { APP_ROUTES } from '../../app-routing.module';
import { AuthLoginResponse } from '../../core/models/auth.models';
import { AuthService } from '../../core/services/auth.service';
import { ProductExperienceModule } from '../../shared/product/product-experience.module';
import { LoginPageComponent } from './login-page.component';
import { PRODUCT_IDENTITY } from '../../core/config/product-identity.config';

describe('LoginPageComponent', () => {
  let fixture: ComponentFixture<LoginPageComponent>;
  let authService: jasmine.SpyObj<AuthService>;
  let router: jasmine.SpyObj<Router>;

  const loginResponse: AuthLoginResponse = {
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    userId: 'user-1',
    expiresAt: '2026-07-14T12:00:00Z',
    roles: [],
    permissions: []
  };

  beforeEach(() => {
    authService = jasmine.createSpyObj<AuthService>('AuthService', ['login']);
    authService.login.and.returnValue(of(loginResponse));
    router = jasmine.createSpyObj<Router>('Router', ['navigateByUrl']);

    TestBed.configureTestingModule({
      declarations: [LoginPageComponent],
      imports: [ButtonModule, NoopAnimationsModule, ProductExperienceModule],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: Router, useValue: router }
      ]
    });

    fixture = TestBed.createComponent(LoginPageComponent);
    fixture.detectChanges();
  });

  it('keeps the existing login route bound to the redesigned Login component', () => {
    const loginRoute = APP_ROUTES.find((route) => route.path === 'login');

    expect(loginRoute?.component).toBe(LoginPageComponent);
  });

  it('uses the shared animated Flowline Login mark instead of page-owned SVG markup', () => {
    const login = fixture.nativeElement.querySelector('.plp-login') as HTMLElement;
    const logo = login.querySelector('plp-brand-logo [data-plp-brand-variant="login"]') as HTMLElement;

    expect(logo).not.toBeNull();
    expect(logo.classList.contains('plp-brand-logo--animated')).toBeTrue();
    expect(login.querySelectorAll(':scope > svg')).toHaveSize(0);
    expect(login.textContent).toContain(PRODUCT_IDENTITY.nameAr);
    expect(logo.getAttribute('aria-label')).toBe(PRODUCT_IDENTITY.logoLabelAr);
  });

  it('submits the existing authentication service exactly once from the rendered form', () => {
    setInputValue('#login-email', 'factory.manager');
    setInputValue('#login-password', 'correct-horse');

    submitLoginForm();

    expect(authService.login).toHaveBeenCalledTimes(1);
    expect(authService.login).toHaveBeenCalledWith('factory.manager', 'correct-horse');
    expect(router.navigateByUrl).toHaveBeenCalledOnceWith('/dashboard');
  });

  it('presents the technical email contract as a plain-text Arabic username field', () => {
    const usernameInput = fixture.nativeElement.querySelector('#login-email') as HTMLInputElement;
    const usernameField = usernameInput.closest('plp-form-field');

    expect(usernameInput.type).toBe('text');
    expect(usernameInput.getAttribute('inputmode')).toBeNull();
    expect(usernameField?.textContent).toContain('اسم المستخدم');
    expect(usernameField?.textContent).not.toContain('البريد الإلكتروني');
  });

  it('keeps the submit action disabled while the existing authentication request is pending', () => {
    const pending = new Subject<AuthLoginResponse>();
    authService.login.and.returnValue(pending);
    setInputValue('#login-email', 'operator@example.com');
    setInputValue('#login-password', 'correct-horse');

    submitLoginForm();
    fixture.detectChanges();

    const submitButton = fixture.nativeElement.querySelector('plp-action-button button') as HTMLButtonElement;
    expect(fixture.componentInstance.isLoading).toBeTrue();
    expect(submitButton.disabled).toBeTrue();

    pending.complete();
  });

  it('blocks duplicate submit events while the existing authentication request is pending', () => {
    const pending = new Subject<AuthLoginResponse>();
    authService.login.and.returnValue(pending);
    setInputValue('#login-email', 'operator@example.com');
    setInputValue('#login-password', 'correct-horse');

    submitLoginForm();
    submitLoginForm();

    expect(authService.login).toHaveBeenCalledTimes(1);

    pending.complete();
  });

  it('renders a translated authentication error without changing the auth contract', () => {
    authService.login.and.returnValue(throwError(() => new HttpErrorResponse({ status: 401 })));
    setInputValue('#login-email', 'operator@example.com');
    setInputValue('#login-password', 'wrong-password');

    submitLoginForm();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('بيانات الدخول غير صحيحة');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('distinguishes an unavailable backend from invalid credentials', () => {
    authService.login.and.returnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    setInputValue('#login-email', 'operator@example.com');
    setInputValue('#login-password', 'correct-horse');

    submitLoginForm();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('تعذر الاتصال بالخادم');
  });

  it('renders a clear Arabic timeout message without exposing transport details', () => {
    authService.login.and.returnValue(throwError(() => new TimeoutError()));
    setInputValue('#login-email', 'operator@example.com');
    setInputValue('#login-password', 'correct-horse');

    submitLoginForm();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('انتهت مهلة الاتصال بالخادم');
  });

  it('toggles the rendered password input accessibly without mutating its value', () => {
    setInputValue('#login-password', 'correct-horse');
    const passwordInput = fixture.nativeElement.querySelector('#login-password') as HTMLInputElement;
    const toggleButton = fixture.nativeElement.querySelector('.plp-login__password-toggle') as HTMLButtonElement;

    expect(passwordInput.type).toBe('password');
    expect(toggleButton.getAttribute('aria-label')).toBe('إظهار كلمة المرور');

    toggleButton.click();
    fixture.detectChanges();

    expect(passwordInput.type).toBe('text');
    expect(passwordInput.value).toBe('correct-horse');
    expect(toggleButton.getAttribute('aria-label')).toBe('إخفاء كلمة المرور');
  });

  it('submits through the native keyboard form path once', () => {
    setInputValue('#login-email', 'operator@example.com');
    setInputValue('#login-password', 'correct-horse');

    submitLoginForm();

    expect(authService.login).toHaveBeenCalledTimes(1);
  });

  function setInputValue(selector: string, value: string): void {
    const input = fixture.nativeElement.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function submitLoginForm(): void {
    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }
});
