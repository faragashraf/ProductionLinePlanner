type UuidCryptoApi = Partial<Pick<Crypto, 'randomUUID' | 'getRandomValues'>>;

/**
 * Produces a backend-compatible RFC 4122 version 4 UUID using Web Crypto.
 * HTTP/LAN browser contexts can expose getRandomValues without randomUUID.
 */
export function generateUuidV4(cryptoApi: UuidCryptoApi | null = globalThis.crypto): string {
  if (typeof cryptoApi?.randomUUID === 'function') {
    return cryptoApi.randomUUID();
  }

  if (typeof cryptoApi?.getRandomValues !== 'function') {
    throw new Error('Secure UUID generation is not supported in this browser.');
  }

  const bytes = new Uint8Array(16);
  cryptoApi.getRandomValues(bytes);
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;

  return Array.from(bytes, (byte, index) => {
    const separator = index === 4 || index === 6 || index === 8 || index === 10 ? '-' : '';
    return `${separator}${byte.toString(16).padStart(2, '0')}`;
  }).join('');
}
