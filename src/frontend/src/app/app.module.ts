import { NgModule } from '@angular/core';
import { HTTP_INTERCEPTORS, HttpClientModule } from '@angular/common/http';
import { BrowserModule } from '@angular/platform-browser';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';

import { AppRoutingModule } from './app-routing.module';
import { AppComponent } from './app.component';
import { DashboardPageComponent } from './pages/dashboard-page/dashboard-page.component';
import { FactoryMapPageComponent } from './pages/factory-map-page/factory-map-page.component';
import { ProductionLinesPageComponent } from './pages/production-lines-page/production-lines-page.component';
import { StagesPageComponent } from './pages/stages-page/stages-page.component';
import { AssignmentsPageComponent } from './pages/assignments-page/assignments-page.component';
import { NotificationsPageComponent } from './pages/notifications-page/notifications-page.component';
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
import { FactoryRendererComponent } from './pages/factory-map-page/renderers/factory-renderer/factory-renderer.component';
import { LineRendererComponent } from './pages/factory-map-page/renderers/line-renderer/line-renderer.component';
import { StageRendererComponent } from './pages/factory-map-page/renderers/stage-renderer/stage-renderer.component';
import { AuthTokenInterceptor } from './core/interceptors/auth-token.interceptor';
import { ConfirmationService, MessageService, PrimeNGConfig } from 'primeng/api';
import { ToastModule } from 'primeng/toast';
import { configureProductionPrimeNg } from './shared/design-system/layering/production-z-index';
import { ProductExperienceModule } from './shared/product/product-experience.module';

@NgModule({
  declarations: [
    AppComponent,
    DashboardPageComponent,
    AccessDeniedPageComponent,
    FactoryMapPageComponent,
    ProductionLinesPageComponent,
    StagesPageComponent,
    AssignmentsPageComponent,
    NotificationsPageComponent,
    LoginPageComponent,
    AppShellComponent,
    FactoryRendererComponent,
    LineRendererComponent,
    StageRendererComponent
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
    SharedModule
  ],
  providers: [
    ConfirmationService,
    MessageService,
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
