export const environment = {
  production: true,
  // Production hosting must expose the API and hubs through the same origin.
  // Keeping these explicit paths avoids a browser-side dependency on an
  // internal API host while preserving deployments mounted below a host name.
  apiBaseUrl: '/api',
  hubBaseUrl: '/hubs'
};
