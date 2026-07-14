export function readRuntimeConfig(runtime = globalThis.LENSEE_CONFIG || {}) {
  const apiBaseUrl = typeof runtime.apiBaseUrl === "string" ? runtime.apiBaseUrl.trim().replace(/\/$/, "") : "";
  const environment = runtime.environment || (globalThis.location?.hostname === "localhost" || globalThis.location?.hostname === "127.0.0.1" ? "development" : "production");
  const isHttpUrl = /^https?:\/\//i.test(apiBaseUrl);
  const isLocalUrl = /^https?:\/\/(localhost|127\.0\.0\.1)(?::\d+)?$/i.test(apiBaseUrl);
  if (!apiBaseUrl || !isHttpUrl) return { apiBaseUrl: "", environment, valid: false, reason: "API base URL is missing or invalid." };
  if (environment === "production" && isLocalUrl) return { apiBaseUrl, environment, valid: false, reason: "Production builds cannot use a localhost API URL." };
  return { apiBaseUrl, environment, valid: true, reason: "" };
}
