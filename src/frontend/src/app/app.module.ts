import { APP_INITIALIZER, NgModule } from '@angular/core';
import { HTTP_INTERCEPTORS, HttpClientModule } from '@angular/common/http';
import { BrowserModule } from '@angular/platform-browser';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';

import { AppRoutingModule } from './app-routing.module';
import { AppComponent } from './app.component';
import { DashboardPageComponent } from './pages/dashboard-page/dashboard-page.component';
import { FactoryMapPageComponent } from './pages/factory-map-page/factory-map-page.component';
import { LoginPageComponent } from './pages/login-page/login-page.component';
import { AccessDeniedPageComponent } from './pages/access-denied-page/access-denied-page.component';
import { AppShellComponent } from './layout/app-shell/app-shell.component';
import { SidebarModule } from 'primeng/sidebar';
import { FocusTrapModule } from 'primeng/focustrap';
import { BreadcrumbModule } from 'primeng/breadcrumb';
import { CardModule } from 'primeng/card';
import { BadgeModule } from 'primeng/badge';
import { ButtonModule } from 'primeng/button';
import { SharedModule } from './shared/shared.module';
import { AuthTokenInterceptor } from './core/interceptors/auth-token.interceptor';
import { ConfirmationService, MessageService, PrimeNGConfig } from 'primeng/api';
import { ToastModule } from 'primeng/toast';
import { configureProductionPrimeNg } from './shared/design-system/layering/production-z-index';
import { ProductExperienceModule } from './shared/product/product-experience.module';
import { RealtimeService } from './core/services/realtime.service';
import { NotificationInboxService } from './core/services/notification-inbox.service';
import { NotificationPresentationService } from './core/services/notification-presentation.service';
import { PlpProductPageHeaderComponent } from './shared/product/plp-page-header.component';

export function initializeRealtimeNotifications(
  realtimeService: RealtimeService,
  notificationInboxService: NotificationInboxService,
  notificationPresentationService: NotificationPresentationService
): () => void {
  return () => {
    realtimeService.initialize();
    notificationInboxService.initialize();
    notificationPresentationService.initialize();
  };
}

@NgModule({
  declarations: [
    AppComponent,
    DashboardPageComponent,
    AccessDeniedPageComponent,
    FactoryMapPageComponent,
    LoginPageComponent,
    AppShellComponent
  ],
  imports: [
    BrowserModule,
    BrowserAnimationsModule,
    HttpClientModule,
    AppRoutingModule,
    SidebarModule,
    FocusTrapModule,
    BreadcrumbModule,
    CardModule,
    BadgeModule,
    ButtonModule,
    ToastModule,
    ProductExperienceModule,
    PlpProductPageHeaderComponent,
    SharedModule
  ],
  providers: [
    ConfirmationService,
    MessageService,
    {
      provide: APP_INITIALIZER,
      useFactory: initializeRealtimeNotifications,
      deps: [RealtimeService, NotificationInboxService, NotificationPresentationService],
      multi: true
    },
    {
      provide: HTTP_INTERCEPTORS,
      useClass: AuthTokenInterceptor,
      multi: true
    }
  ],
  bootstrap: [AppComponent]
})
export class AppModule {
  constructor(config: PrimeNGConfig) {
    configureProductionPrimeNg(config);
  }
}
