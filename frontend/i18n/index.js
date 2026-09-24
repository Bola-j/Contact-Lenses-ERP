import en from "./en.js?v=20260924-i18n-final";
import ar from "./ar.js?v=20260924-i18n-final";

const languageKey = "lensee.language";
const dictionaries = Object.freeze({ en, ar });

function normalizeLanguage(language) {
  return language === "en" ? "en" : "ar";
}

export function getLanguage() {
  return normalizeLanguage(localStorage.getItem(languageKey));
}

export function setLanguage(language) {
  const normalized = normalizeLanguage(language);
  localStorage.setItem(languageKey, normalized);
  document.documentElement.lang = normalized === "ar" ? "ar-EG" : "en";
  document.documentElement.dir = normalized === "ar" ? "rtl" : "ltr";
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
