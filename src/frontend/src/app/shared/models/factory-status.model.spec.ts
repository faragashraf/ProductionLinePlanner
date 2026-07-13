import { resolveFactoryStatus, resolveFactoryStatusTone } from './factory-status.model';

describe('Factory status visual tone contract', () => {
  it('maps domain labels to the shared visual-tone contract', () => {
    const status = resolveFactoryStatus('late');

    expect(status.tone).toBe('warning');
    expect(resolveFactoryStatusTone(status.status).primeSeverity).toBe('warning');
  });

  it('keeps unknown factory statuses safely neutral', () => {
    expect(resolveFactoryStatus('unknown').tone).toBe('neutral');
  });
});
