import { Component } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { PRODUCT_IDENTITY } from './core/config/product-identity.config';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent {
  constructor(title: Title) {
    title.setTitle(`${PRODUCT_IDENTITY.nameEn} - ${PRODUCT_IDENTITY.nameAr}`);
  }
}
