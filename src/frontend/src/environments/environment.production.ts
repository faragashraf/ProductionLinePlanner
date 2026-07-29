export const environment = {
  production: true,
  // IIS hosts the static application and API as separate LAN sites. These are
  // origins, not IIS physical-path segments, so the Angular base href remains
  // `/` and the URL builders append `/api` or `/hubs` as appropriate.
  apiBaseUrl: 'http://192.168.1.99:9000',
  hubBaseUrl: 'http://192.168.1.99:9000'
};
