import { formatDateTime as formatSharedDateTime } from "../date-format.js?v=20261014-date-format";
import en from "./en.js?v=20261019-shopify-ops-refresh-4";
import ar from "./ar.js?v=20261019-shopify-ops-refresh-4";

const languageKey = "lensee.language";
const dictionaries = Object.freeze({ en, ar });
const reportedMissingKeys = new Set();

function normalizeLanguage(language) {
  return language === "en" ? "en" : "ar";
}

function prefixLanguage() {
  try {
    if (typeof window === "undefined") return null;
    const segment = window.location.pathname.split("/").filter(Boolean)[0]?.toLowerCase() || null;
    if (segment === "ar" || segment === "en") return segment;
  } catch { /* non-browser embedding: fall through to default */ }
  return null;
}

function urlHelperLanguage() {
  try {
    return typeof window === "undefined" ? null : (window.LenseeUrlLanguage?.language || null);
  } catch { return null; }
}

export function getLanguage() {
  // URL prefix owns the language. No stored preference is consulted so a
  // shared /ar/ or /en/ link always renders the same presentation language.
  return normalizeLanguage(prefixLanguage() || urlHelperLanguage() || "en");
}

export function setLanguage(language) {
  // Language switching is a deliberate navigation between URL prefixes. It
  // preserves query string and hash route; transient form state is not kept.
  const normalized = normalizeLanguage(language);
  if (typeof window === "undefined") return normalized;
  const target = window.LenseeUrlLanguage?.toLanguage(normalized);
  if (target) {
    window.location.assign(target);
    return normalized;
  }
  const fallback = new URL(window.location.href);
  fallback.pathname = `/${normalized}/`;
  window.location.assign(fallback.href);
  return normalized;
}

export function t(key, params = {}) {
  const normalized = normalizeLanguage(getLanguage());
  const message = dictionaries[normalized][key] ?? dictionaries.en[key] ?? missingKeyFallback(key, normalized);
  return String(message).replace(/\{([A-Za-z0-9_]+)\}/g, (match, name) =>
    Object.prototype.hasOwnProperty.call(params, name) ? String(params[name]) : match);
}

function missingKeyFallback(key, language) {
  reportMissingKey(key, language);
  return readableMissingKey(key);
}

function reportMissingKey(key, language) {
  const normalizedKey = String(key ?? "");
  const marker = `${language}:${normalizedKey}`;
  if (reportedMissingKeys.has(marker)) return;
  reportedMissingKeys.add(marker);
  if (typeof console !== "undefined" && typeof console.warn === "function") {
    console.warn(`[i18n] Missing translation key "${normalizedKey}" for ${language}.`);
  }
}

function readableMissingKey(key) {
  return String(key ?? "")
    .split(".")
    .pop()
    .replace(/[_-]+/g, " ")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .trim() || String(key ?? "");
}

export function formatNumber(value, options) {
  return new Intl.NumberFormat(getLanguage() === "ar" ? "ar-EG" : "en-US", options).format(value);
}

export function formatDate(value, options = { dateStyle: "medium", timeStyle: "short" }) {
  if (options?.timeStyle) return formatSharedDateTime(value instanceof Date ? value.toISOString() : value);
  return formatSharedDateTime(value instanceof Date ? value.toISOString().slice(0, 10) : value).split(" ")[0];
}

export function isRtl() {
  return getLanguage() === "ar";
}

export { languageKey };
