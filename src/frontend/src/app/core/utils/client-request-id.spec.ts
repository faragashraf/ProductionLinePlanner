import { createClientRequestId } from './client-request-id';

describe('createClientRequestId', () => {
  const guidV4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

  it('returns a valid GUID when randomUUID is unavailable on an HTTP/LAN browser', () => {
    const cryptoFallback = {
      getRandomValues: (values: Uint8Array) => {
        values.fill(0x11);
        return values;
      }
    } as unknown as Pick<Crypto, 'getRandomValues'>;
    const id = createClientRequestId(cryptoFallback);

    expect(id).toMatch(guidV4);
  });

  it('keeps a valid GUID contract even on the legacy entropy fallback', () => {
    expect(createClientRequestId(null)).toMatch(guidV4);
  });
});
