(() => {
  const language = localStorage.getItem("lensee.language") === "en" ? "en" : "ar";
  document.documentElement.lang = language === "ar" ? "ar-EG" : "en";
  document.documentElement.dir = language === "ar" ? "rtl" : "ltr";
})();
