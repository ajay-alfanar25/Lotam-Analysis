// Development config, used by `npm start` (ng serve --proxy-config
// proxy.conf.json). apiBaseUrl is deliberately empty here - requests stay
// relative ('/api/...') and proxy.conf.json forwards them to the local API
// (http://localhost:5000) so there's no cross-origin request at all in dev.
export const environment = {
  production: false,
  apiBaseUrl: '',
};
