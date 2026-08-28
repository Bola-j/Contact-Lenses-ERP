import { spawnSync } from "node:child_process";
import { platform } from "node:process";

const [scriptPath, ...scriptArguments] = process.argv.slice(2);

if (!scriptPath) {
  console.error("Usage: node scripts/run-powershell-script.mjs <script.ps1> [arguments]");
  process.exit(64);
}

const executable = platform === "win32" ? "powershell.exe" : "pwsh";
const result = spawnSync(
  executable,
  ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath, ...scriptArguments],
  { stdio: "inherit" },
);

if (result.error) {
  console.error(`Unable to start ${executable}: ${result.error.message}`);
  process.exit(1);
}

process.exit(result.status ?? 1);
