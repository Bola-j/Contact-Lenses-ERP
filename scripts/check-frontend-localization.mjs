import fs from "node:fs";

const appPath = "frontend/app.js";
const htmlPath = "frontend/index.html";
const app = fs.readFileSync(appPath, "utf8");
const html = fs.readFileSync(htmlPath, "utf8");
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

const requiredDynamicRules = [
  "Showing\\s+",
  "was added as an active warehouse",
  "Delete account",
  "the primary Administrator",
  "sessions?",
  "events?",
  "recalls?"
];
const missingDynamicRules = requiredDynamicRules.filter((fragment) => !applicationSource.includes(fragment));

if (missing.length || missingDynamicRules.length) {
  console.error("Frontend localization check failed.");
  if (missing.length) {
    console.error("English UI text without an Arabic translation:");
    for (const text of missing) console.error(`- ${text}`);
  }
  if (missingDynamicRules.length) console.error(`Missing dynamic translation rules: ${missingDynamicRules.join(", ")}`);
  process.exit(1);
}

if (duplicates.length) console.warn(`Frontend localization note: ${duplicates.length} legacy duplicate keys use their final declared translation.`);
console.log(`Frontend localization check passed for ${translations.size} Arabic translations and ${candidates.size} checked UI strings.`);
