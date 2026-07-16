export const environment = {
  production: false,
  // Development requests stay on the browser origin and are forwarded by the
  // Angular development server. This works equally from loopback and a LAN
  // device without embedding the Mac's current address in the bundle.
  apiBaseUrl: '/api',
  hubBaseUrl: '/hubs'
};
