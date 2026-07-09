import { NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';

import { AppRoutingModule } from './app-routing.module';
import { AppComponent } from './app.component';
import { DashboardPageComponent } from './pages/dashboard-page/dashboard-page.component';
import { FactoryMapPageComponent } from './pages/factory-map-page/factory-map-page.component';
import { ProductionLinesPageComponent } from './pages/production-lines-page/production-lines-page.component';
import { StagesPageComponent } from './pages/stages-page/stages-page.component';
import { WorkersPageComponent } from './pages/workers-page/workers-page.component';
import { AssignmentsPageComponent } from './pages/assignments-page/assignments-page.component';
import { NotificationsPageComponent } from './pages/notifications-page/notifications-page.component';
import { LoginPageComponent } from './pages/login-page/login-page.component';
import { AppShellComponent } from './layout/app-shell/app-shell.component';
import { SidebarModule } from 'primeng/sidebar';
import { BreadcrumbModule } from 'primeng/breadcrumb';
import { CardModule } from 'primeng/card';
import { BadgeModule } from 'primeng/badge';
import { ButtonModule } from 'primeng/button';
import { SharedModule } from './shared/shared.module';

@NgModule({
  declarations: [
    AppComponent,
    DashboardPageComponent,
    FactoryMapPageComponent,
    ProductionLinesPageComponent,
    StagesPageComponent,
    WorkersPageComponent,
    AssignmentsPageComponent,
    NotificationsPageComponent,
    LoginPageComponent,
    AppShellComponent
  ],
  imports: [
    BrowserModule,
    BrowserAnimationsModule,
    AppRoutingModule,
    SidebarModule,
    BreadcrumbModule,
    CardModule,
    BadgeModule,
    ButtonModule,
    SharedModule
  ],
  providers: [],
  bootstrap: [AppComponent]
})
export class AppModule { }
