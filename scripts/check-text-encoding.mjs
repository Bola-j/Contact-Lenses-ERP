import fs from "node:fs";
import path from "node:path";

const root = process.cwd();
const includedRoots = ["frontend", "docs", "scripts", "e2e"];
const includedFiles = ["package.json", "package-lock.json", "playwright.config.js", ".editorconfig"];
const textExtensions = new Set([
  ".cs",
  ".css",
  ".html",
  ".js",
  ".json",
  ".md",
  ".mjs",
  ".ps1",
  ".sql",
  ".txt",
  ".yml"
]);

const mojibakePatterns = [
  { label: "replacement character", pattern: /\uFFFD/ },
  { label: "Arabic UTF-8 decoded as Windows-1252", pattern: /[\u00C3\u00C2\u00D8\u00D9][\u0080-\uFFFF]/ },
  { label: "punctuation UTF-8 decoded as Windows-1252", pattern: /\u00E2[\u0080-\uFFFF]{1,3}/ }
];

function* walk(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      if (["node_modules", ".git", "bin", "obj", "test-results", "playwright-report"].includes(entry.name)) continue;
      yield* walk(fullPath);
    } else if (entry.isFile()) {
      yield fullPath;
    }
  }
}

function isTextFile(filePath) {
  return textExtensions.has(path.extname(filePath).toLowerCase());
}

const files = [
  ...includedFiles.map((file) => path.join(root, file)).filter((file) => fs.existsSync(file)),
  ...includedRoots
    .map((directory) => path.join(root, directory))
    .filter((directory) => fs.existsSync(directory))
    .flatMap((directory) => [...walk(directory)])
    .filter(isTextFile)
];

const failures = [];

for (const filePath of files) {
  const text = fs.readFileSync(filePath, "utf8");
  for (const { label, pattern } of mojibakePatterns) {
    const match = pattern.exec(text);
    if (!match) continue;

    const before = text.slice(0, match.index);
    const line = before.split(/\r\n|\n|\r/).length;
    const column = match.index - Math.max(before.lastIndexOf("\n"), before.lastIndexOf("\r")) + 1;
    failures.push(`${path.relative(root, filePath)}:${line}:${column} ${label}`);
    break;
  }
}

if (failures.length > 0) {
  console.error("Text encoding check failed. Possible mojibake found:");
  for (const failure of failures) console.error(`- ${failure}`);
  process.exit(1);
}

console.log(`Text encoding check passed for ${files.length} files.`);
