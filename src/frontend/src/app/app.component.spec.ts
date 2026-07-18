import { Title } from '@angular/platform-browser';
import { PRODUCT_IDENTITY } from './core/config/product-identity.config';
import { AppComponent } from './app.component';

describe('AppComponent', () => {
  it('uses the English and Arabic product names in the browser page title', () => {
    const title = jasmine.createSpyObj<Title>('Title', ['setTitle']);

    new AppComponent(title);

    expect(title.setTitle).toHaveBeenCalledOnceWith(
      `${PRODUCT_IDENTITY.nameEn} - ${PRODUCT_IDENTITY.nameAr}`
    );
  });
});
