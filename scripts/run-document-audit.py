#!/usr/bin/env python3
"""Run a UI-first, evidence-only audit of Lensee reports and documents.

Credentials are read only from LENSEE_AUDIT_USERNAME and LENSEE_AUDIT_PASSWORD.
Raw files are deliberately written below tmp/; tracked evidence contains hashes and
derived checks only.  The script never writes operational business data.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import re
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from openpyxl import load_workbook
from playwright.sync_api import Browser, Page, sync_playwright
from pypdf import PdfReader


ROOT = Path(__file__).resolve().parents[1]
UUID = re.compile(r"\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b", re.I)
RAW_JSON = re.compile(r"\{\s*\"[^\"]+\"\s*:")
INTERNAL_VALUE = re.compile(r"\b(?:PendingApproval|CashHandToHand|MerchantAccount|WholesaleSale|RetailSale|PartiallyReceived)\b")
EXPECTED_SCHEMAS = {"catalog", "crm", "finance", "identity", "inventory", "notifications", "operations", "payments", "reporting", "shared"}
ENTITY_ENDPOINTS = {
    "operation-bill": "/api/v1/reports/operations?page=1&pageSize=100000",
    "payment-receipt": "/api/v1/reports/payments?page=1&pageSize=100000",
    "cash-receipt": "/api/v1/reports/payments?page=1&pageSize=100000",
    "supply-landed-cost": "/api/v1/reports/supply?page=1&pageSize=100000",
    "merchant-statement": "/api/v1/crm/merchants?includeInactive=true&page=1&pageSize=100000",
    "stocktake-summary": "/api/v1/stocktakes?page=1&pageSize=100000",
}
LEGACY_PATHS = {
    "stock": "/api/v1/reports/stock.csv?page=1&pageSize=100000",
    "operations": "/api/v1/reports/operations.csv?page=1&pageSize=100000",
    "payments": "/api/v1/reports/payments.csv?page=1&pageSize=100000",
    "supply": "/api/v1/reports/supply.csv?page=1&pageSize=100000",
    "merchant-balances": "/api/v1/reports/merchant-balances.csv?page=1&pageSize=100000",
}
LEGACY_DOCUMENT_PATHS = {
    "operation-bill": "/api/v1/reports/operations/{id}/bill.pdf?language={language}",
    "payment-receipt": "/api/v1/reports/payments/{id}/receipt.pdf?language={language}",
    "cash-receipt": "/api/v1/reports/payments/{id}/cash-receipt.pdf?language={language}",
    "supply-landed-cost": "/api/v1/reports/supply/{id}/landed-cost.pdf?language={language}",
    "merchant-statement": "/api/v1/reports/merchants/{id}/statement.pdf?language={language}",
    "stocktake-summary": "/api/v1/reports/stocktakes/{id}/summary.pdf?language={language}",
}


def now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat()


def outcome(status: str, check: str, detail: str, **extra: Any) -> dict[str, Any]:
    return {"status": status, "check": check, "detail": detail, **extra}


def read_database_snapshot() -> dict[str, Any]:
    sql = "select table_schema || '.' || table_name from information_schema.tables where table_type = 'BASE TABLE' order by 1"
    command = ["docker", "exec", "lensee_db", "sh", "-lc", 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At -c "' + sql + '"']
    try:
        result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=30, check=False)
    except (OSError, subprocess.TimeoutExpired) as exc:
        return {"status": "BLOCKED", "detail": f"Cannot inspect the local DEV database: {exc}", "tables": []}
    if result.returncode:
        return {"status": "BLOCKED", "detail": "Database inspection failed without exposing connection details.", "tables": []}
    tables = [line.strip() for line in result.stdout.splitlines() if line.strip()]
    schemas = {value.split(".", 1)[0] for value in tables}
    missing = sorted(EXPECTED_SCHEMAS - schemas)
    counts: dict[str, int] = {}
    if not missing:
        count_sql = " union all ".join(
            f"select '{table}' as source, count(*)::text as rows from {table}"
            for table in ["catalog.skus", "crm.merchants", "inventory.stock_balances", "inventory.opened_piece_lots", "operations.operation_logs", "operations.supply_shipments", "operations.stocktake_sessions", "payments.main_payment_logs", "payments.merchant_account_entries"]
        )
        count_command = ["docker", "exec", "lensee_db", "sh", "-lc", 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At -F "|" -c "' + count_sql + '"']
        count_result = subprocess.run(count_command, cwd=ROOT, capture_output=True, text=True, timeout=30, check=False)
        if count_result.returncode == 0:
            counts = {line.split("|", 1)[0]: int(line.split("|", 1)[1]) for line in count_result.stdout.splitlines() if "|" in line}
    merchant_entry_breakdown: dict[str, int] = {}
    if "payments" in schemas:
        breakdown_command = ["docker", "exec", "lensee_db", "sh", "-lc", "psql -v ON_ERROR_STOP=1 -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -At -F \"|\" -c \"select entry_type, count(*) from payments.merchant_account_entries where status = 'Posted' group by entry_type order by entry_type\""]
        breakdown_result = subprocess.run(breakdown_command, cwd=ROOT, capture_output=True, text=True, timeout=30, check=False)
        if breakdown_result.returncode == 0:
            merchant_entry_breakdown = {line.split("|", 1)[0]: int(line.split("|", 1)[1]) for line in breakdown_result.stdout.splitlines() if "|" in line}
    return {
        "status": "PASS" if not missing else "BLOCKED",
        "detail": "Operational schemas are available." if not missing else f"Missing operational schemas: {', '.join(missing)}.",
        "tables": tables,
        "schemas": sorted(schemas),
        "missingSchemas": missing,
        "rowCounts": counts,
        "merchantEntryBreakdown": merchant_entry_breakdown,
    }


def api_json(page: Page, path: str) -> tuple[int, Any]:
    return page.evaluate("""async (path) => {
      const response = await fetch(path, { headers: { 'X-Document-Audit': 'true' } });
      const text = await response.text();
      try { return [response.status, JSON.parse(text)]; } catch { return [response.status, { raw: text.slice(0, 400) }]; }
    }""", path)


def save_download(download: Any, path: Path) -> dict[str, Any]:
    path.parent.mkdir(parents=True, exist_ok=True)
    download.save_as(str(path))
    data = path.read_bytes()
    return {"fileName": download.suggested_filename, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def extracted_text(path: Path) -> tuple[str, int]:
    suffix = path.suffix.lower()
    if suffix == ".pdf":
        reader = PdfReader(str(path))
        return "\n".join(page.extract_text() or "" for page in reader.pages), len(reader.pages)
    if suffix == ".xlsx":
        workbook = load_workbook(path, read_only=True, data_only=True)
        values = []
        for sheet in workbook.worksheets:
            for row in sheet.iter_rows(values_only=True):
                values.extend("" if value is None else str(value) for value in row)
        return "\n".join(values), len(workbook.worksheets)
    return path.read_text(encoding="utf-8", errors="replace"), 1


def inspect_file(path: Path, language: str) -> dict[str, Any]:
    try:
        text, pages = extracted_text(path)
    except Exception as exc:  # renderer/parser failure is audit evidence, not a crash
        return {"parse": outcome("FAIL", "rendered-document-readable", str(exc)), "textLength": 0}
    issues = []
    if UUID.search(text): issues.append("raw UUID")
    if RAW_JSON.search(text): issues.append("raw JSON")
    if INTERNAL_VALUE.search(text): issues.append("unlocalized internal value")
    if language == "ar" and not re.search(r"[\u0600-\u06ff]", text): issues.append("Arabic export lacks Arabic text")
    if language == "en" and re.search(r"[\u0600-\u06ff]", text): issues.append("English export contains unexpected Arabic text")
    return {
        "rendered": outcome("PASS" if not issues else "FAIL", "rendered-document-presentation", "No presentation leaks." if not issues else "; ".join(issues)),
        "pagesOrSheets": pages,
        "textLength": len(text),
    }


def wait_for_report_idle(page: Page) -> None:
    page.wait_for_timeout(500)
    page.locator("#report-export-status").wait_for(state="visible", timeout=20_000)


def ui_export(page: Page, selector: str, language: str, destination: Path) -> dict[str, Any]:
    page.locator(f'input[name="report-export-language"][value="{language}"]').check()
    wait_for_report_idle(page)
    button = page.locator(selector)
    if button.count() == 0:
        return {"ui": outcome("FAIL", "SPA-download-control", "The report catalog exposes this export, but the Reports SPA has no matching export control.", selector=selector)}
    try:
        # Chromium does not reliably emit a Download event for the SPA's Blob URL.
        # The UI result remains the browser-first proof; callers then fetch the same
        # authorized endpoint for byte-level renderer inspection.
        button.click()
        for _ in range(60):
            status = page.locator("#report-export-status").inner_text(timeout=2_000)
            if "completed" in status.lower() or "downloaded" in status.lower():
                return {"ui": outcome("PASS", "SPA-download", "The Reports SPA confirmed the download.", docket=status)}
            if "error" in status.lower() or "failed" in status.lower():
                return {"ui": outcome("FAIL", "SPA-download", status)}
            page.wait_for_timeout(500)
        return {"ui": outcome("FAIL", "SPA-download", "The Reports SPA did not confirm completion within 30 seconds.")}
    except Exception as exc:
        status = page.locator("#report-export-status").inner_text(timeout=2_000) if page.locator("#report-export-status").count() else ""
        notice = page.locator("#notification-area").inner_text(timeout=2_000) if page.locator("#notification-area").count() else ""
        return {"ui": outcome("FAIL", "SPA-download", str(exc), docket=status, notice=notice)}


def fetch_entities(page: Page, document_key: str) -> tuple[int, list[dict[str, Any]]]:
    status, body = api_json(page, ENTITY_ENDPOINTS[document_key])
    rows = body.get("items", body) if isinstance(body, dict) else body
    if not isinstance(rows, list): rows = []
    if document_key == "cash-receipt":
        rows = [row for row in rows if row.get("paymentMethod") == "CashHandToHand"]
    return status, rows


def entity_id(key: str, row: dict[str, Any]) -> str:
    return str(row.get("merchantId") or row.get("id") or "") if key == "merchant-statement" else str(row.get("id") or "")


def direct_document(page: Page, path: str, destination: Path, language: str) -> dict[str, Any]:
    """Request legacy/doc endpoints through the authenticated browser and save bytes safely."""
    result = page.evaluate("""async (path) => {
      async function resolve(path) {
        let r = await fetch(path, { headers: { 'X-Document-Audit': 'true' } });
        if (r.status !== 202) return r;
        const job = await r.json();
        for (let i = 0; i < 60; i++) {
          await new Promise(resolve => setTimeout(resolve, 500));
          const p = await fetch(`/api/v1/reports/document-jobs/${job.id}`, { headers: { 'X-Document-Audit': 'true' } });
          const state = await p.json();
          if (state.status === 'Ready') return fetch(`/api/v1/reports/document-jobs/${job.id}/file`, { headers: { 'X-Document-Audit': 'true' } });
          if (state.status === 'Failed') return new Response(state.error || 'Document job failed.', { status: 500 });
        }
        return new Response('Document job timed out.', { status: 504 });
      }
      const response = await resolve(path);
      const buffer = new Uint8Array(await response.arrayBuffer());
      let binary = ''; for (const byte of buffer) binary += String.fromCharCode(byte);
      return { status: response.status, type: response.headers.get('content-type') || '', body: btoa(binary) };
    }""", path)
    if result["status"] >= 400:
        return {"direct": outcome("FAIL", "direct-route", f"HTTP {result['status']}", path=path)}
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(base64.b64decode(result["body"]))
    metadata = {"bytes": destination.stat().st_size, "sha256": hashlib.sha256(destination.read_bytes()).hexdigest(), "contentType": result["type"]}
    metadata.update(inspect_file(destination, language))
    metadata["direct"] = outcome("PASS", "direct-route", "Authenticated browser request completed.", path=path)
    return metadata


def status_counts(records: list[dict[str, Any]]) -> dict[str, int]:
    result = {"PASS": 0, "FAIL": 0, "BLOCKED": 0, "NOT EXECUTED": 0}
    for record in records:
        if record.get("status") in result: result[record["status"]] += 1
        for value in record.values():
            if isinstance(value, dict) and value.get("status") in result: result[value["status"]] += 1
    return result


def render_markdown(run: dict[str, Any]) -> str:
    counts = status_counts(run["checks"])
    lines = ["# Document Audit Report", "", f"- Run: `{run['startedAt']}`", f"- SPA: `{run['baseUrl']}`", f"- Snapshot: `{run['database']['status']}` — {run['database']['detail']}", "", "## Result summary", "", "| PASS | FAIL | BLOCKED | NOT EXECUTED |", "| ---: | ---: | ---: | ---: |", f"| {counts['PASS']} | {counts['FAIL']} | {counts['BLOCKED']} | {counts['NOT EXECUTED']} |", "", "## Findings", ""]
    findings = [check for check in run["checks"] if check.get("status") in {"FAIL", "BLOCKED"} or any(isinstance(value, dict) and value.get("status") in {"FAIL", "BLOCKED"} for value in check.values())]
    if not findings: lines.append("No failures or blockers were recorded.")
    for finding in findings:
        label = finding.get("id", finding.get("check", "audit-check"))
        details = [finding.get("detail", "")] if finding.get("status") in {"FAIL", "BLOCKED"} else []
        details += [value.get("detail", "") for value in finding.values() if isinstance(value, dict) and value.get("status") in {"FAIL", "BLOCKED"}]
        lines.append(f"- **{label}** — {'; '.join(details)}")
    lines += ["", "## Scope", "", "Catalog exports and direct routes are attempted only after an authenticated SPA session is established. Database truth is never inferred when operational schemas are unavailable."]
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default=os.getenv("LENSEE_AUDIT_BASE_URL", "http://localhost:3001"))
    parser.add_argument("--username-env", default="LENSEE_AUDIT_USERNAME")
    parser.add_argument("--password-env", default="LENSEE_AUDIT_PASSWORD")
    args = parser.parse_args()
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    evidence = ROOT / "artifacts" / "overnight" / "document-audit" / stamp
    raw = ROOT / "tmp" / "document-audit" / stamp
    evidence.mkdir(parents=True, exist_ok=True); raw.mkdir(parents=True, exist_ok=True)
    run: dict[str, Any] = {"startedAt": now(), "baseUrl": args.base_url.rstrip("/"), "database": read_database_snapshot(), "checks": []}
    if run["database"].get("merchantEntryBreakdown", {}).get("ReturnCredit", 0):
        run["checks"].append(outcome(
            "BLOCKED", "merchant-statement-return-credit-presentation",
            "Posted ReturnCredit entries exist. The current statement summary must be rendered and reconciled before deciding whether its sales/adjustment labels account for those credits.",
            returnCredits=run["database"]["merchantEntryBreakdown"]["ReturnCredit"],
        ))
    username, password = os.getenv(args.username_env), os.getenv(args.password_env)
    if not username or not password:
        run["checks"].append(outcome("BLOCKED", "authenticated-SPA-export", f"Set {args.username_env} and {args.password_env} to run exports without recording credentials."))
    else:
        with sync_playwright() as playwright:
            browser: Browser = playwright.chromium.launch(headless=True)
            page = browser.new_page(accept_downloads=True, viewport={"width": 1440, "height": 950})
            console_errors: list[str] = []
            page.on("console", lambda message: console_errors.append(message.text) if message.type == "error" else None)
            try:
                page.goto(run["baseUrl"] + "/en/#/login", wait_until="networkidle", timeout=30_000)
                page.locator("#username").fill(username); page.locator("#password").fill(password); page.locator("#login-submit").click()
                page.wait_for_url(re.compile(r"#/dashboard"), timeout=30_000)
                # Login refreshes session state and may perform one final dashboard navigation.
                # Settling it first prevents that navigation from cancelling the report route.
                page.wait_for_timeout(1_500)
                for attempt in range(3):
                    try:
                        page.goto(run["baseUrl"] + "/en/#/reports", wait_until="domcontentloaded", timeout=30_000)
                        page.locator("#report-page-size").wait_for(state="visible", timeout=30_000)
                        break
                    except Exception:
                        if attempt == 2: raise
                        page.wait_for_timeout(1_000)
                page.locator("#report-page-size").select_option("100000"); page.wait_for_timeout(1_000)
                page.screenshot(path=str(evidence / "reports-spa.png"), full_page=True)
                leaks = page.evaluate("() => window.__lenseeFindVisibleUuidLeaks ? window.__lenseeFindVisibleUuidLeaks() : ['presentation scanner unavailable']")
                run["checks"].append(outcome("PASS" if not leaks else "FAIL", "SPA-visible-identifiers", "No visible UUID leaks." if not leaks else str(leaks)))
                catalog_status, catalog = api_json(page, "/api/v1/reports/catalog")
                if catalog_status != 200 or not isinstance(catalog, list):
                    run["checks"].append(outcome("FAIL", "report-catalog", f"Expected catalog HTTP 200; received {catalog_status}."))
                else:
                    run["catalog"] = catalog
                    for descriptor in catalog:
                        key = descriptor["key"]
                        for language in descriptor.get("languages", []):
                            for fmt in descriptor.get("formats", []):
                                record: dict[str, Any] = {"id": f"catalog:{key}:{fmt}:{language}", "key": key, "format": fmt, "language": language}
                                if descriptor.get("isDocument"):
                                    status, rows = fetch_entities(page, key)
                                    record["lookup"] = outcome("PASS" if status == 200 else "FAIL", "document-lookup", f"HTTP {status}; {len(rows)} eligible records.")
                                    if len(rows) > 50: record["pickerCoverage"] = outcome("FAIL", "SPA-document-picker", f"{len(rows)} eligible records but the SPA picker loads only 50.")
                                    if not rows: record["export"] = outcome("BLOCKED", "document-export", "No eligible current record exists for this document type.")
                                    else:
                                        # The direct document endpoint provides exhaustive per-entity coverage; the first record is also exercised through the SPA control.
                                        for index, row in enumerate(rows):
                                            identifier = entity_id(key, row)
                                            if not identifier: continue
                                            ext = "xlsx" if fmt == "excel" else fmt
                                            direct = direct_document(page, f"/api/v1/documents/{key}/{identifier}?format={fmt}&language={language}", raw / f"{key}-{index}-{language}.{ext}", language)
                                            run["checks"].append({"id": f"document:{key}:{index}:{fmt}:{language}", **direct})
                                            if fmt == "pdf":
                                                legacy_path = LEGACY_DOCUMENT_PATHS[key].format(id=identifier, language=language)
                                                legacy = direct_document(page, legacy_path, raw / f"legacy-{key}-{index}-{language}.pdf", language)
                                                run["checks"].append({"id": f"legacy-document:{key}:{index}:{language}", **legacy})
                                        hidden = f"#report-picker-{key}-value"
                                        if page.locator(hidden).count():
                                            page.locator(hidden).evaluate("(node, value) => { node.value = value; node.dispatchEvent(new Event('change', { bubbles: true })); }", entity_id(key, rows[0]))
                                            record.update(ui_export(page, f'[data-pdf-report="{key}"][data-export-format="{fmt}"]', language, raw / f"spa-{key}-{language}.{ext}"))
                                else:
                                    record.update(ui_export(page, f'[data-download-report="{key}"][data-export-format="{fmt}"]', language, raw / f"spa-{key}-{language}.{fmt}"))
                                    inspect_path = f"/api/v1/reports/{key}/export?format={fmt}&language={language}&pageSize=100000"
                                    extension = "xlsx" if fmt == "excel" else fmt
                                    record.update({"artifact": direct_document(page, inspect_path, raw / f"{key}-{language}.{extension}", language)})
                                run["checks"].append(record)
                    for key, path in LEGACY_PATHS.items():
                        run["checks"].append({"id": f"legacy:{key}", **direct_document(page, path, raw / f"legacy-{key}.csv", "en")})
                if console_errors:
                    run["checks"].append(outcome("FAIL", "browser-console", "; ".join(console_errors[:10])))
            except Exception as exc:
                login_error = page.locator("#login-error").inner_text(timeout=1_000) if page.locator("#login-error").count() else ""
                page.screenshot(path=str(evidence / "spa-audit-blocked.png"), full_page=True)
                run["checks"].append(outcome("BLOCKED", "SPA-audit-session", login_error or str(exc)))
            finally:
                browser.close()
    run["finishedAt"] = now()
    (evidence / "document-audit-matrix.json").write_text(json.dumps(run, indent=2), encoding="utf-8")
    report = render_markdown(run)
    (evidence / "DOCUMENT_AUDIT_REPORT.md").write_text(report, encoding="utf-8")
    (ROOT / "artifacts" / "overnight" / "DOCUMENT_AUDIT_REPORT.md").write_text(
        report + f"\nLatest detailed evidence: `artifacts/overnight/document-audit/{stamp}/`\n", encoding="utf-8")
    findings = [value for value in run["checks"] if value.get("status") in {"FAIL", "BLOCKED"} or any(isinstance(item, dict) and item.get("status") in {"FAIL", "BLOCKED"} for item in value.values())]
    (evidence / "DOCUMENT_AUDIT_FINDINGS.json").write_text(json.dumps(findings, indent=2), encoding="utf-8")
    print(evidence)
    return 0


if __name__ == "__main__":
    sys.exit(main())
