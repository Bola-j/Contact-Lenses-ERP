import fs from "node:fs";

const cp1252 = new Map([
  [0x20ac, 0x80],
  [0x201a, 0x82],
  [0x0192, 0x83],
  [0x201e, 0x84],
  [0x2026, 0x85],
  [0x2020, 0x86],
  [0x2021, 0x87],
  [0x02c6, 0x88],
  [0x2030, 0x89],
  [0x0160, 0x8a],
  [0x2039, 0x8b],
  [0x0152, 0x8c],
  [0x017d, 0x8e],
  [0x2018, 0x91],
  [0x2019, 0x92],
  [0x201c, 0x93],
  [0x201d, 0x94],
  [0x2022, 0x95],
  [0x2013, 0x96],
  [0x2014, 0x97],
  [0x02dc, 0x98],
  [0x2122, 0x99],
  [0x0161, 0x9a],
  [0x203a, 0x9b],
  [0x0153, 0x9c],
  [0x017e, 0x9e],
  [0x0178, 0x9f]
]);

const cp1252SpecialChars = "\u20AC\u201A\u0192\u201E\u2026\u2020\u2021\u02C6\u2030\u0160\u2039\u0152\u017D\u2018\u2019\u201C\u201D\u2022\u2013\u2014\u02DC\u2122\u0161\u203A\u0153\u017E\u0178";
const encodableRun = `[\\u0080-\\u00ff${cp1252SpecialChars}]`;
const suspicious = new RegExp(`[\\u00C2\\u00C3\\u00D8\\u00D9\\u00E2]${encodableRun}+`, "g");

function toCp1252Bytes(text) {
  const bytes = [];
  for (const char of text) {
    const code = char.codePointAt(0);
    if (code <= 0xff) {
      bytes.push(code);
    } else if (cp1252.has(code)) {
      bytes.push(cp1252.get(code));
    } else {
      return null;
    }
  }
  return Buffer.from(bytes);
}

function repairText(text) {
  return text.replace(suspicious, (match) => {
    const bytes = toCp1252Bytes(match);
    if (!bytes) return match;

    const repaired = bytes.toString("utf8");
    return repaired.includes("\uFFFD") ? match : repaired;
  });
}

const files = process.argv.slice(2);
if (files.length === 0) {
  console.error("Usage: node scripts/repair-mojibake.mjs <file> [file...]");
  process.exit(2);
}

for (const file of files) {
  const original = fs.readFileSync(file, "utf8");
  const repaired = repairText(original);
  if (repaired !== original) {
    fs.writeFileSync(file, repaired, "utf8");
    console.log(`repaired ${file}`);
  } else {
    console.log(`unchanged ${file}`);
  }
}

