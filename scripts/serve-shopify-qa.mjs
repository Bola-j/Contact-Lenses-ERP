// Local QA reverse proxy serves the actual SPA and API under one origin. No mocked responses.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../frontend');
const port = Number(process.env.SHOPIFY_QA_FRONTEND_PORT || 53001);
const apiPort = Number(process.env.SHOPIFY_QA_API_PORT || 55001);
const mime = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.svg': 'image/svg+xml', '.png': 'image/png', '.woff2': 'font/woff2' };

http.createServer((request, response) => {
  const pathname = new URL(request.url, `http://127.0.0.1:${port}`).pathname;
  if (/^\/(api\/|ready$|health$)/.test(pathname)) {
    const upstream = http.request({ hostname: '127.0.0.1', port: apiPort, path: request.url, method: request.method,
      headers: { ...request.headers, host: `127.0.0.1:${apiPort}` } }, incoming => {
      response.writeHead(incoming.statusCode, incoming.headers);
      incoming.pipe(response);
    });
    upstream.on('error', () => { response.writeHead(502); response.end('QA API unavailable.'); });
    request.pipe(upstream);
    return;
  }
  const filename = path.resolve(root, '.' + decodeURIComponent(pathname));
  if (!filename.startsWith(root + path.sep) && filename !== root) { response.writeHead(403); response.end(); return; }
  const target = fs.existsSync(filename) && fs.statSync(filename).isFile() ? filename :
    !path.extname(filename) ? path.join(root, 'index.html') : null;
  if (!target) { response.writeHead(404); response.end(); return; }
  response.writeHead(200, { 'Content-Type': mime[path.extname(target)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
  fs.createReadStream(target).pipe(response);
}).listen(port, '127.0.0.1', () => process.stdout.write(`Shopify QA frontend http://127.0.0.1:${port}; actual API :${apiPort}\n`));
