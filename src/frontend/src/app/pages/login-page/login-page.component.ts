import { Component } from '@angular/core';
import { Router } from '@angular/router';

@Component({
  selector: 'app-login-page',
  templateUrl: './login-page.component.html',
  styleUrls: ['./login-page.component.scss']
})
export class LoginPageComponent {
  email = 'placeholder@local.test';
  password = '••••••';

  constructor(private readonly router: Router) {}

  onLogin(event: Event): void {
    event.preventDefault();
    this.router.navigateByUrl('/dashboard');
  }
}
