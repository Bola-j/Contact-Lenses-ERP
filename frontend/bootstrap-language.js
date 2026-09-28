(() => {
  // URL ownership is authoritative for presentation language.
  // The first pathname segment (/ar/ or /en/) owns the language; stored
  // preferences are never consulted so a shared link always renders the same.
  const DEFAULT_LANGUAGE = "en";
  const firstPathSegment = () => location.pathname.split("/").filter(Boolean)[0]?.toLowerCase() || null;
  const prefixLanguage = () => {
    const segment = firstPathSegment();
    return segment === "ar" || segment === "en" ? segment : null;
  };
  const canonicalUrl = (language, hash = location.hash || "#/dashboard") => {
    const target = new URL(location.href);
    target.pathname = `/${language}/`;
    target.hash = hash;
    return target.href;
  };
  const applyLanguage = (language) => {
    document.documentElement.lang = language === "ar" ? "ar-EG" : "en";
    document.documentElement.dir = language === "ar" ? "rtl" : "ltr";
  };

  // Drop any legacy stored preference so future loads cannot diverge from URL.
  try { localStorage.removeItem("lensee.language"); } catch { /* storage may be unavailable */ }

  const language = prefixLanguage() || DEFAULT_LANGUAGE;
  applyLanguage(language);

  // Only root/legacy URLs and recognized language prefixes reach the SPA.
  // A recognized prefix may have case or extra pathname segments, but business
  // navigation remains exclusively in the hash and is normalized before app.js.
  const prefix = prefixLanguage();
  const isLegacyRoot = location.pathname === "/";
  const isCanonicalPrefix = prefix === language && location.pathname === `/${language}/` && Boolean(location.hash);
  if ((isLegacyRoot || prefix) && !isCanonicalPrefix) {
    location.replace(canonicalUrl(language));
    return;
  }

  window.LenseeUrlLanguage = Object.freeze({
    language,
    toLanguage(languageToUse) {
      const normalized = languageToUse === "en" ? "en" : "ar";
      return canonicalUrl(normalized, location.hash || "#/dashboard");
    }
  });
})();
