import { defineConfig, type Connect } from 'vite';

// The same /config.json an nginx container serves from the environment (see
// nginx.conf.template), so the app reads its settings the same way in
// development and in Docker, and the built bundle holds none of them.
function umbralConfig() {
  // Read when the dev or preview server starts, not at build time: a build
  // needs neither variable.
  const middleware = (): Connect.NextHandleFunction => {
    const issuer = process.env.UMBRAL_ISSUER;
    const clientId = process.env.UMBRAL_CLIENT_ID;
    if (!issuer) throw new Error('UMBRAL_ISSUER is not set: set it to your Umbral\'s issuer URL.');
    if (!clientId) throw new Error('UMBRAL_CLIENT_ID is not set: set it to the app\'s Client ID.');
    const body = JSON.stringify({ issuer, clientId });
    return (req, res, next) => {
      if (req.method !== 'GET' || req.url?.split('?')[0] !== '/config.json') return next();
      res.setHeader('Content-Type', 'application/json');
      res.setHeader('Cache-Control', 'no-store');
      res.end(body);
    };
  };
  return {
    name: 'umbral-config',
    configureServer(server: { middlewares: Connect.Server }) {
      server.middlewares.use(middleware());
    },
    configurePreviewServer(server: { middlewares: Connect.Server }) {
      server.middlewares.use(middleware());
    },
  };
}

const port = Number(process.env.PORT ?? 5173);

export default defineConfig({
  plugins: [umbralConfig()],
  server: { port, strictPort: true },
  preview: { port, strictPort: true },
});
