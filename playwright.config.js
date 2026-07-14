// @ts-check
const { defineConfig, devices } = require("@playwright/test");

const frontendPort = Number(process.env.LENSEE_E2E_FRONTEND_PORT || 3000);
const frontendBaseUrl = process.env.LENSEE_E2E_FRONTEND_URL || `http://127.0.0.1:${frontendPort}`;

module.exports = defineConfig({
  testDir: "./e2e",
  timeout: 90_000,
  expect: {
    timeout: 12_000
  },
  fullyParallel: false,
  workers: 1,
  reporter: [
    ["list"],
    ["html", { open: "never", outputFolder: "playwright-report" }]
  ],
  use: {
    baseURL: frontendBaseUrl,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure"
  },
  webServer: process.env.LENSEE_E2E_SKIP_WEBSERVER === "1" ? undefined : {
    command: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/serve-frontend.ps1 -Port ${frontendPort}`,
    url: frontendBaseUrl,
    reuseExistingServer: true,
    timeout: 30_000
  },
  projects: [
    {
      name: "chromium",
      testIgnore: /.*mobile.*\.spec\.js/,
      use: { ...devices["Desktop Chrome"], viewport: { width: 1440, height: 950 } }
    },
    {
      name: "mobile-chromium",
      use: { ...devices["Pixel 7"] },
      testMatch: /.*mobile.*\.spec\.js/
    }
  ]
});
