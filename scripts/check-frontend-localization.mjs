import fs from "node:fs";
import { canonicalSystemValue } from "../frontend/localization.js";
import en from "../frontend/i18n/en.js";
import ar from "../frontend/i18n/ar.js";

const appPath = "frontend/app.js";
const htmlPath = "frontend/index.html";
const app = fs.readFileSync(appPath, "utf8");
const html = fs.readFileSync(htmlPath, "utf8");
const enKeys = Object.keys(en);
const arKeys = Object.keys(ar);
const semanticDictionaryDuplicates = ["en", "ar"].flatMap((language) => {
  const source = fs.readFileSync(`frontend/i18n/${language}.js`, "utf8");
  const keys = [...source.matchAll(/^\s*,?"([^"]+)"\s*:/gm)].map((match) => match[1]);
  return [...new Set(keys.filter((key, index) => keys.indexOf(key) !== index))]
    .map((key) => `${language}:${key}`);
});
const missingArabicKeys = enKeys.filter((key) => !(key in ar));
const extraArabicKeys = arKeys.filter((key) => !(key in en));
const foundationKeys = new Set(enKeys);
const foundationKeyUses = [
  ...[...app.matchAll(/\bfoundationT\(\s*["']([^"']+)["']/g)].map((match) => match[1]),
  ...[...html.matchAll(/data-i18n(?:-[\w-]+)?="([^"]+)"/g)].map((match) => match[1])
];
const unknownFoundationKeys = [...new Set(foundationKeyUses.filter((key) => !foundationKeys.has(key)))];
const foundationEnglishValues = new Set(Object.values(en));
const staticUiTextValues = [...new Set([...app.matchAll(/\buiText\(\s*["']([^"']+)["']\s*\)/g)].map((match) => match[1]))];
const literalPromptTitles = [...applicationPromptTitles(app)];
const literalConfirmTitles = [...app.matchAll(/\bconfirmDialog\(\{[\s\S]{0,160}?\btitle:\s*["']([^"']+)["']/g)].map((match) => match[1]);
const literalFormErrors = [...app.matchAll(/\bshowFormError\(\s*["'][^"']+["']\s*,\s*["']([A-Za-z][^"']*)["']/g)].map((match) => match[1]);

function* applicationPromptTitles(source) {
  for (const match of source.matchAll(/\bpromptDialog\(\{[\s\S]{0,160}?\btitle:\s*["']([^"']+)["']/g))
    yield match[1];
}
const unmigratedStaticUiText = staticUiTextValues.filter((value) => !foundationEnglishValues.has(value));
const dictionaryStart = app.indexOf("const arabicTranslations = Object.freeze({");
const dictionaryEnd = app.indexOf("\n});", dictionaryStart);

if (dictionaryStart < 0 || dictionaryEnd < 0) {
  console.error("Frontend localization check failed: Arabic translation dictionary was not found.");
  process.exit(1);
}

const dictionarySource = app.slice(dictionaryStart, dictionaryEnd);
const applicationSource = app.slice(dictionaryEnd + 4);
const entries = [...dictionarySource.matchAll(/^\s*("(?:\\.|[^"\\])*")\s*:\s*("(?:\\.|[^"\\])*")/gm)]
  .map((match) => [JSON.parse(match[1]), JSON.parse(match[2])]);
const translations = new Map(entries);
const duplicates = entries
  .map(([key]) => key)
  .filter((key, index, keys) => keys.indexOf(key) !== index)
  .filter((key, index, keys) => keys.indexOf(key) === index);
const optionsWithoutCanonicalValue = [...applicationSource.matchAll(/<option(?![^>]*\bvalue=)[^>]*>\s*[A-Za-z][^<]*<\/option>/g)]
  .map((match) => match[0]);
const canonicalContractFailures = [];
for (const [value, domain] of [["RetailSale", "operationType"], ["MerchantAccount", "paymentMethod"], ["WarehouseClerk", "role"], ["Retail", "locationType"]]) {
  try {
    if (canonicalSystemValue(value, domain) !== value) canonicalContractFailures.push(`${domain}:${value}`);
  } catch {
    canonicalContractFailures.push(`${domain}:${value}`);
  }
}
for (const [value, domain] of [["بيع قطاعي / أونلاين", "operationType"], ["تقسيط", "paymentMethod"], ["أمين المخزن", "role"]]) {
  try {
    canonicalSystemValue(value, domain);
    canonicalContractFailures.push(`${domain}:${value}`);
  } catch {
    // Translated labels must never be accepted as API values.
  }
}

const candidates = new Set();
const ignored = new Set([
  "Lensee",
  "Lensee ERP",
  "English",
  "Page of",
  "Switch to Arabic",
  "Switch to English",
  "Was:",
  "by",
  "x"
]);

function normalize(value) {
  return value
    .replace(/&amp;/g, "&")
    .replace(/&nbsp;/g, " ")
    .replace(/\$\{[^{}]*\}/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function addCandidate(value) {
  const text = normalize(value);
  if (foundationKeys.has(text)) return;
  if (!text || ignored.has(text) || !/[A-Za-z]/.test(text)) return;
  if (/[{}=<>]/.test(text) || /^[-/?.#\[\]]/.test(text)) return;
  if (/^[a-z0-9]+(?:-[a-z0-9]+)+$/.test(text)) return;
  if (/^(?:GET|POST|PUT|PATCH|DELETE|Bearer|Content-Type|application\/json)$/i.test(text)) return;
  candidates.add(text);
}

function collectHtmlText(source) {
  for (const match of source.matchAll(/>([^<>]+)</g)) addCandidate(match[1]);
  for (const match of source.matchAll(/(?:placeholder|title|aria-label)=(?:"([^"]+)"|'([^']+)')/g)) {
    addCandidate(match[1] || match[2]);
  }
}

for (const match of applicationSource.matchAll(/`([\s\S]*?)`/g)) {
  if (match[1].includes("<")) collectHtmlText(match[1]);
}
collectHtmlText(html.replace(/<[^>]+data-no-translate[^>]*>[\s\S]*?<\/[^>]+>/g, ""));

for (const match of applicationSource.matchAll(/\b(?:eyebrow|title|body|label)\s*:\s*"((?:\\.|[^"\\])*)"/g)) {
  addCandidate(JSON.parse(`"${match[1]}"`));
}
for (const match of applicationSource.matchAll(/\bscenarioCard\(\s*"((?:\\.|[^"\\])*)"\s*,\s*"((?:\\.|[^"\\])*)"/g)) {
  addCandidate(JSON.parse(`"${match[1]}"`));
  addCandidate(JSON.parse(`"${match[2]}"`));
}
for (const match of applicationSource.matchAll(/\.(?:textContent|innerText)\s*=\s*"((?:\\.|[^"\\])*)"/g)) {
  addCandidate(JSON.parse(`"${match[1]}"`));
}
for (const match of applicationSource.matchAll(/\b(?:notice|showFormError|window\.prompt|window\.confirm)\(\s*"((?:\\.|[^"\\])*)"/g)) {
  addCandidate(JSON.parse(`"${match[1]}"`));
}

const missing = [...candidates]
  .filter((text) => !translations.has(text))
  .sort((left, right) => left.localeCompare(right));

// Dynamic presentation must be rendered from semantic foundation keys. The
// previous validator required regex translation rules, which encouraged the
// legacy document-wide translator this check is intended to prevent.
const requiredSemanticKeys = ["pagination.pageOf", "payments.pageOf"];
const missingSemanticKeys = requiredSemanticKeys.filter((key) => !applicationSource.includes(`foundationT("${key}"`));

if (missing.length || missingSemanticKeys.length || unmigratedStaticUiText.length || literalPromptTitles.length || literalConfirmTitles.length || literalFormErrors.length || duplicates.length || semanticDictionaryDuplicates.length || optionsWithoutCanonicalValue.length || canonicalContractFailures.length || applicationSource.includes("systemValueAliases") || missingArabicKeys.length || extraArabicKeys.length || unknownFoundationKeys.length) {
  console.error("Frontend localization check failed.");
  if (missing.length) {
    console.error("English UI text without an Arabic translation:");
    for (const text of missing) console.error(`- ${text}`);
  }
  if (missingSemanticKeys.length) console.error(`Missing semantic translation keys: ${missingSemanticKeys.join(", ")}`);
  if (unmigratedStaticUiText.length) {
    console.error("Static uiText phrases without a foundation semantic key:");
    for (const text of unmigratedStaticUiText) console.error(`- ${text}`);
  }
  if (literalPromptTitles.length) console.error(`Prompt titles without semantic translation keys: ${literalPromptTitles.join(", ")}`);
  if (literalConfirmTitles.length) console.error(`Confirmation titles without semantic translation keys: ${literalConfirmTitles.join(", ")}`);
  if (literalFormErrors.length) console.error(`Form errors without semantic translation keys: ${literalFormErrors.join(", ")}`);
  if (duplicates.length) console.error(`Duplicate Arabic translation keys: ${duplicates.join(", ")}`);
  if (semanticDictionaryDuplicates.length) console.error(`Duplicate semantic translation keys: ${semanticDictionaryDuplicates.join(", ")}`);
  if (optionsWithoutCanonicalValue.length) {
    console.error("System options without explicit canonical values:");
    for (const option of optionsWithoutCanonicalValue) console.error(`- ${option}`);
  }
  if (canonicalContractFailures.length) console.error(`Canonical value contract failures: ${canonicalContractFailures.join(", ")}`);
  if (applicationSource.includes("systemValueAliases")) console.error("Arabic-to-English system value aliases are not allowed.");
  if (missingArabicKeys.length) console.error(`Missing Arabic foundation keys: ${missingArabicKeys.join(", ")}`);
  if (extraArabicKeys.length) console.error(`Unexpected Arabic foundation keys: ${extraArabicKeys.join(", ")}`);
  if (unknownFoundationKeys.length) console.error(`Unknown foundation translation keys: ${unknownFoundationKeys.join(", ")}`);
  process.exit(1);
}

console.log(`Frontend localization check passed for ${translations.size} Arabic translations and ${candidates.size} checked UI strings.`);
