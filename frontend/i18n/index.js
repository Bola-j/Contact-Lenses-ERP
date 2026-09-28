import en from "./en.js?v=20260927-supply-receiving";
import ar from "./ar.js?v=20260927-supply-receiving";

const languageKey = "lensee.language";
const dictionaries = Object.freeze({ en, ar });

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
  const message = dictionaries[normalized][key] ?? dictionaries.en[key] ?? key;
  return String(message).replace(/\{([A-Za-z0-9_]+)\}/g, (match, name) =>
    Object.prototype.hasOwnProperty.call(params, name) ? String(params[name]) : match);
}

export function formatNumber(value, options) {
  return new Intl.NumberFormat(getLanguage() === "ar" ? "ar-EG" : "en-US", options).format(value);
}

export function formatDate(value, options = { dateStyle: "medium", timeStyle: "short" }) {
  return new Intl.DateTimeFormat(getLanguage() === "ar" ? "ar-EG" : "en-US", options)
    .format(value instanceof Date ? value : new Date(value));
}

export function isRtl() {
  return getLanguage() === "ar";
}

export { languageKey };
