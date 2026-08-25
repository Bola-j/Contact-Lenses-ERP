import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";

const frontendRoot = join(process.cwd(), "frontend");
const legacyApp = join(frontendRoot, "app.js");
const maximumLegacyInnerHtmlUses = 157;
const unsafeSink = /\b(?:innerHTML|outerHTML|insertAdjacentHTML|document\.write(?:ln)?|eval|Function)\b/g;

const legacyMatches = readFileSync(legacyApp, "utf8").match(/\binnerHTML\b/g)?.length ?? 0;
if (legacyMatches > maximumLegacyInnerHtmlUses) {
  throw new Error(`frontend/app.js has ${legacyMatches} innerHTML uses; the approved migration baseline is ${maximumLegacyInnerHtmlUses}. Replace new rendering with DOM APIs and lower the baseline as legacy paths are removed.`);
}

for (const file of readdirSync(frontendRoot, { recursive: true })) {
  if (typeof file !== "string" || !file.endsWith(".js") || file === "app.js") continue;
  const path = join(frontendRoot, file);
  const matches = [...readFileSync(path, "utf8").matchAll(unsafeSink)];
  if (matches.length > 0) {
    throw new Error(`${path} introduces an unsafe DOM or dynamic-code sink: ${matches.map(match => match[0]).join(", ")}`);
  }
}

console.log(`Unsafe DOM sink guard passed (${legacyMatches}/${maximumLegacyInnerHtmlUses} legacy innerHTML uses).`);
