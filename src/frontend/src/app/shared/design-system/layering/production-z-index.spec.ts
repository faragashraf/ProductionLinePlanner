import { PrimeNGConfig } from 'primeng/api';
import { configureProductionPrimeNg } from './production-z-index';

describe('configureProductionPrimeNg', () => {
  it('preserves PrimeNG nested ARIA translations while applying Arabic PLP labels', () => {
    const config = new PrimeNGConfig();
    const defaultListLabel = config.translation.aria?.listLabel;
    const defaultZoomLabel = config.translation.aria?.zoomIn;

    configureProductionPrimeNg(config);

    expect(config.translation.aria?.listLabel).toBe('قائمة الخيارات');
    expect(config.translation.aria?.zoomIn).toBe(defaultZoomLabel);
    expect(config.translation.aria?.listLabel).not.toBe(defaultListLabel);
    expect(config.translation.aria?.rowsPerPageLabel).toBe('عدد الصفوف في الصفحة');
    expect(config.translation.aria?.nextPageLabel).toBe('الصفحة التالية');
  });
});
