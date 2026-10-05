// Production config, swapped in for environment.ts at build time via
// angular.json's "production" fileReplacements (see `ng build`).
//
// The frontend (Cloudflare Pages) and API (Render, or wherever it ends up
// running) are on two different domains once deployed, so - unlike dev,
// where proxy.conf.json makes everything look same-origin - every request
// needs the API's actual origin baked in here. There is no server-side
// templating for a static host, so this has to be a real value at build
// time, not an env var read at runtime.
//
// EDIT THIS before your first production build: replace the placeholder
// with your actual deployed API URL (see DEPLOYMENT.md "Frontend: Cloudflare
// Pages" step). No trailing slash.
export const environment = {
  production: true,
  apiBaseUrl: 'https://lotam-sentinel-api.onrender.com',
};
