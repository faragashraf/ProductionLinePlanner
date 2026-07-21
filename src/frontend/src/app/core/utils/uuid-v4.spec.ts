import { generateUuidV4 } from './uuid-v4';

describe('generateUuidV4', () => {
  const uuidV4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

  it('uses crypto.randomUUID when it is available', () => {
    const randomUUID = jasmine.createSpy('randomUUID').and.returnValue('00112233-4455-4677-8899-aabbccddeeff');
    const getRandomValues = jasmine.createSpy('getRandomValues');

    expect(generateUuidV4({ randomUUID, getRandomValues } as unknown as Crypto)).toBe('00112233-4455-4677-8899-aabbccddeeff');
    expect(randomUUID).toHaveBeenCalledTimes(1);
    expect(getRandomValues).not.toHaveBeenCalled();
  });

  it('uses getRandomValues without calling an unavailable randomUUID in an HTTP/LAN browser', () => {
    const getRandomValues = jasmine.createSpy('getRandomValues').and.callFake((bytes: Uint8Array) => {
      bytes.set([0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x76, 0x77, 0xff, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff]);
      return bytes;
    });

    const uuid = generateUuidV4({ getRandomValues } as unknown as Crypto);

    expect(uuid).toBe('00112233-4455-4677-bf99-aabbccddeeff');
    expect(uuid).toMatch(uuidV4);
    expect(uuid[14]).toBe('4');
    expect(uuid[19].toLowerCase()).toMatch(/[89ab]/);
    expect(getRandomValues).toHaveBeenCalledTimes(1);
  });

  it('throws a clear error when no secure Web Crypto UUID capability is available', () => {
    expect(() => generateUuidV4(null)).toThrowError('Secure UUID generation is not supported in this browser.');
  });
});
