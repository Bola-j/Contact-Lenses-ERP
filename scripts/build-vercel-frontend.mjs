import { writeFileSync } from 'node:fs';

const isProductionDeploy = Boolean(process.env.VERCEL) || process.env.NODE_ENV === 'production';
const apiBaseUrl = process.env.LENSEE_API_BASE_URL || process.env.VITE_API_BASE_URL || (isProductionDeploy ? '' : 'http://localhost:5000');

if (!apiBaseUrl) {
  throw new Error('LENSEE_API_BASE_URL is required for production frontend builds. Use a public HTTPS API URL.');
}

if (isProductionDeploy && /localhost|127\.0\.0\.1/.test(apiBaseUrl)) {
  throw new Error(`LENSEE_API_BASE_URL cannot point to localhost in production: ${apiBaseUrl}`);
}

writeFileSync(
  'frontend/config.js',
  `window.LENSEE_CONFIG = {\n  apiBaseUrl: ${JSON.stringify(apiBaseUrl)}\n};\n`,
  'utf8',
);

console.log(`Generated frontend/config.js with apiBaseUrl=${apiBaseUrl}`);
