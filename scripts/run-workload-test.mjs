import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import { performance } from "node:perf_hooks";

const defaultBaseUrl = "http://127.0.0.1:55000";
const defaultPassword = "E2E-only-not-production-2026!";
const liveTargetConfirmation = "I_UNDERSTAND_THIS_HITS_A_LIVE_SYSTEM";
const workloadRoutes = [
  "/api/v1/auth/me",
  "/api/v1/catalog/categories",
  "/api/v1/catalog/brands",
  "/api/v1/catalog/products",
  "/api/v1/notifications/unread-count"
];

function optionValue(name) {
  const index = process.argv.indexOf(name);
  return index === -1 ? undefined : process.argv[index + 1];
}

function hasOption(name) {
  return process.argv.includes(name);
}

function positiveInteger(name, value, minimum = 1) {
  const parsed = Number(value);
  if (!Number.isInteger(parsed) || parsed < minimum) {
    throw new Error(`${name} must be an integer greater than or equal to ${minimum}.`);
  }
  return parsed;
}

function nonNegativeNumber(name, value) {
  const parsed = Number(value);
  if (!Number.isFinite(parsed) || parsed < 0) {
    throw new Error(`${name} must be a non-negative number.`);
  }
  return parsed;
}

function percentile(values, percent) {
  if (values.length === 0) return null;
  const sorted = [...values].sort((left, right) => left - right);
  return Math.round(sorted[Math.min(sorted.length - 1, Math.ceil(sorted.length * percent) - 1)]);
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function timestampForFileName() {
  return new Date().toISOString().replace(/[:.]/g, "-");
}

async function request(url, options, timeoutMs) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  const startedAt = performance.now();
  try {
    const response = await fetch(url, { ...options, signal: controller.signal });
    await response.arrayBuffer();
    return {
      elapsedMs: Math.round(performance.now() - startedAt),
      status: response.status,
      success: response.ok
    };
  } catch (error) {
    return {
      elapsedMs: Math.round(performance.now() - startedAt),
      status: error.name === "AbortError" ? "timeout" : "network-error",
      success: false
    };
  } finally {
    clearTimeout(timer);
  }
}

async function login(baseUrl, username, password, timeoutMs) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(`${baseUrl}/api/v1/auth/login`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ username, password }),
      signal: controller.signal
    });
    const body = response.ok ? await response.json() : null;
    if (!response.ok || typeof body?.accessToken !== "string") {
      throw new Error(`Login for ${username} returned HTTP ${response.status}.`);
    }
    return body.accessToken;
  } finally {
    clearTimeout(timer);
  }
}

async function monitorReadiness(baseUrl, timeoutMs, monitor) {
  while (monitor.active) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    const startedAt = performance.now();
    let probe;
    try {
      const response = await fetch(`${baseUrl}/ready`, {
        headers: { accept: "application/json" },
        signal: controller.signal
      });
      const body = response.ok ? await response.json() : null;
      probe = {
        elapsedMs: Math.round(performance.now() - startedAt),
        status: response.status,
        success: response.ok && body?.status === "Healthy"
      };
    } catch (error) {
      probe = {
        elapsedMs: Math.round(performance.now() - startedAt),
        status: error.name === "AbortError" ? "timeout" : "network-error",
        success: false
      };
    } finally {
      clearTimeout(timer);
    }
    monitor.probes += 1;
    if (!probe.success) {
      monitor.failures += 1;
      monitor.results.push({ status: probe.status, elapsedMs: probe.elapsedMs });
    }
    await delay(5000);
  }
}

async function runStage(config, users) {
  const startedAt = new Date().toISOString();
  const tokens = await Promise.all(users.map((username) => login(config.baseUrl, username, config.password, config.timeoutMs)));
  const requests = [];
  const monitor = { active: true, probes: 0, failures: 0, results: [] };
  const monitorPromise = monitorReadiness(config.baseUrl, config.timeoutMs, monitor);
  const deadline = performance.now() + config.durationSeconds * 1000;

  await Promise.all(tokens.map(async (token, userIndex) => {
    let iteration = 0;
    while (performance.now() < deadline) {
      const route = workloadRoutes[(userIndex + iteration) % workloadRoutes.length];
      const result = await request(`${config.baseUrl}${route}`, {
        headers: { authorization: `Bearer ${token}`, accept: "application/json" }
      }, config.timeoutMs);
      requests.push({ route, ...result });
      iteration += 1;
      if (config.thinkMs > 0) await delay(config.thinkMs);
    }
  }));

  monitor.active = false;
  await monitorPromise;

  const latencies = requests.map((entry) => entry.elapsedMs);
  const failedRequests = requests.filter((entry) => !entry.success);
  const statusCounts = Object.fromEntries(Object.entries(requests.reduce((counts, entry) => {
    counts[`${entry.route} ${entry.status}`] = (counts[`${entry.route} ${entry.status}`] ?? 0) + 1;
    return counts;
  }, {})).sort(([left], [right]) => left.localeCompare(right)));
  const errorRate = requests.length === 0 ? 1 : failedRequests.length / requests.length;
  const summary = {
    users: users.length,
    startedAt,
    completedAt: new Date().toISOString(),
    totalRequests: requests.length,
    failedRequests: failedRequests.length,
    errorRate: Number(errorRate.toFixed(4)),
    statusCounts,
    latencyMs: {
      p50: percentile(latencies, 0.5),
      p95: percentile(latencies, 0.95),
      p99: percentile(latencies, 0.99),
      max: latencies.length === 0 ? null : Math.max(...latencies)
    },
    readiness: {
      probes: monitor.probes,
      failures: monitor.failures,
      failedProbeResults: monitor.results
    }
  };
  summary.passed = summary.readiness.failures === 0
    && summary.errorRate <= config.maxErrorRate
    && summary.latencyMs.p95 !== null
    && summary.latencyMs.p95 <= config.maxP95Ms;
  return summary;
}

function loadConfig() {
  const baseUrl = (optionValue("--base-url") ?? process.env.LENSEE_WORKLOAD_API_URL ?? defaultBaseUrl).replace(/\/$/, "");
  const users = positiveInteger("--users", optionValue("--users") ?? "8");
  const stagesValue = optionValue("--stages") ?? String(users);
  const stages = stagesValue.split(",").map((value) => positiveInteger("--stages", value.trim()));
  const maxUsers = Math.max(...stages);
  const explicitUsers = process.env.LENSEE_WORKLOAD_USERNAMES?.split(",").map((value) => value.trim()).filter(Boolean);
  const usernames = explicitUsers?.length
    ? explicitUsers
    : Array.from({ length: maxUsers }, (_, index) => `e2e_load_${String(index + 1).padStart(2, "0")}`);

  if (usernames.length < maxUsers) {
    throw new Error(`The workload needs ${maxUsers} usernames but only ${usernames.length} were supplied.`);
  }

  const isDefaultTarget = baseUrl === defaultBaseUrl;
  if (!isDefaultTarget && (!hasOption("--allow-live-target") || process.env.LENSEE_ALLOW_LIVE_WORKLOAD_TEST !== liveTargetConfirmation)) {
    throw new Error("A non-E2E target requires --allow-live-target and LENSEE_ALLOW_LIVE_WORKLOAD_TEST=I_UNDERSTAND_THIS_HITS_A_LIVE_SYSTEM.");
  }

  const password = process.env.LENSEE_WORKLOAD_PASSWORD ?? (isDefaultTarget ? defaultPassword : undefined);
  if (!password) {
    throw new Error("Set LENSEE_WORKLOAD_PASSWORD for a non-E2E target; never place it on the command line.");
  }

  const maxErrorRate = nonNegativeNumber("--max-error-rate", optionValue("--max-error-rate") ?? "0.01");
  if (maxErrorRate > 1) {
    throw new Error("--max-error-rate cannot exceed 1.");
  }

  return {
    baseUrl,
    usernames,
    password,
    stages,
    durationSeconds: positiveInteger("--duration-seconds", optionValue("--duration-seconds") ?? "60"),
    thinkMs: nonNegativeNumber("--think-ms", optionValue("--think-ms") ?? "500"),
    timeoutMs: positiveInteger("--timeout-ms", optionValue("--timeout-ms") ?? "10000"),
    maxP95Ms: positiveInteger("--max-p95-ms", optionValue("--max-p95-ms") ?? "2000"),
    maxErrorRate,
    outputPath: optionValue("--output") ?? path.join("artifacts", "workload", `workload-${timestampForFileName()}.json`),
    dryRun: hasOption("--dry-run")
  };
}

const config = loadConfig();
const publicConfig = {
  baseUrl: config.baseUrl,
  stages: config.stages,
  durationSeconds: config.durationSeconds,
  thinkMs: config.thinkMs,
  timeoutMs: config.timeoutMs,
  maxP95Ms: config.maxP95Ms,
  maxErrorRate: config.maxErrorRate,
  usernames: config.usernames,
  routes: workloadRoutes
};

if (config.dryRun) {
  console.log(JSON.stringify({ dryRun: true, ...publicConfig }, null, 2));
  process.exitCode = 0;
} else {
  const report = { startedAt: new Date().toISOString(), config: publicConfig, stages: [] };
  for (const userCount of config.stages) {
    console.log(`Running ${userCount} authenticated read-only virtual users for ${config.durationSeconds} seconds...`);
    try {
      const stage = await runStage(config, config.usernames.slice(0, userCount));
      report.stages.push(stage);
      console.log(`Stage ${userCount}: requests=${stage.totalRequests}; errors=${stage.failedRequests}; p95=${stage.latencyMs.p95}ms; readyFailures=${stage.readiness.failures}; passed=${stage.passed}`);
      if (!stage.passed) break;
    } catch (error) {
      report.stages.push({ users: userCount, passed: false, fatalError: error.message });
      break;
    }
  }
  report.completedAt = new Date().toISOString();
  report.passed = report.stages.length === config.stages.length && report.stages.every((stage) => stage.passed);
  await mkdir(path.dirname(config.outputPath), { recursive: true });
  await writeFile(config.outputPath, `${JSON.stringify(report, null, 2)}\n`, "utf8");
  console.log(`Evidence: ${config.outputPath}`);
  if (!report.passed) process.exitCode = 1;
}
