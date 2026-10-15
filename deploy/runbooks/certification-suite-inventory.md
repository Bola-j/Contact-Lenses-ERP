# Release certification suite inventory

This inventory describes the release/nightly certification path after the certification refactor. All jobs check out the exact candidate SHA. The complete browser suite, runtime/deployment rehearsal, resolved production configuration, and isolated backup/restore drill remain independent release blockers. A passing pull-request CI run is not a substitute for those release suites.

## Retained evidence and removed duplicate invocations

| Previous check or coverage | New evidence owner | Treatment |
| --- | --- | --- |
| Restore, Release build with analyzers, formatting, backend unit/contract tests | `CI / Backend build, tests, and format` on the exact successful main push SHA | Removed from release certification; CI is the required same-SHA gate. |
| PostgreSQL integration suite and three individually repeated transaction/concurrency/constraint tests | `CI / PostgreSQL integration tests` on the exact successful main push SHA | Removed from release certification; the focused filters were already included in the complete integration suite. |
| npm install, frontend guards, deterministic frontend build | `CI / Frontend checks and build` on the exact successful main push SHA | Removed from release certification. |
| Image SBOM and HIGH/CRITICAL scan; Semgrep SAST | Candidate-image SBOM/Trivy scan plus exact-SHA `Security Evidence` and `Semgrep SAST` workflows, checked by `Certification / Same-SHA security evidence` | The candidate image is scanned from the verified bundle used by runtime/recovery/publication. Existing push-time Security Evidence and Semgrep runs are also required for that exact SHA. |
| Admin/login/role and critical checkout smoke | `CI / Critical UI smoke` | Unchanged and still a required PR check; full browser certification remains release-blocking. |
| Production Compose shape, loopback database binding, schema-mutation safeguards, persistent Data Protection storage, Caddy and alert configuration | `Certification / Release configuration` | Retained once per certification candidate, using synthetic throwaway settings. |
| API/frontend candidate image build | `Certification / Build candidate image bundle` | Built once per candidate and passed as a SHA-256-verified image archive into runtime/recovery; the same tested image IDs are published only after all release gates pass. |
| VPS-shaped Compose deployment, health/readiness, explicit migration behavior, idempotent second deploy, injected migration failure, eight-user workload, default-stack non-interference, actual resource policy, API image runtime policy | `Certification / Runtime and deployment` | Retained. Resource limits now come from resolved Compose config and are independently checked against actual containers and the runner capacity envelope. |
| Full Playwright browser and business workflow suite | `Certification / Complete browser suite` | Retained in full; no business tests were deleted or rewritten. `npm run e2e` remains the suite entry point. |
| Synthetic backup, restore, migration, readiness, and data reconciliation | `Certification / Backup and restore recovery` | Retained with a logged checkpoint/result for each recovery step, failure-stage summary, bounded diagnostics, and exact isolated-project cleanup. |
| Local API image build and Trivy scan formerly embedded in Phase 4-10 | CI/security workflows and the release image publication job | Removed from the duplicate script. The actual running candidate API still gets non-root, timezone-file, and immutable image-ID runtime checks. |

The old `verify-phase4-10.ps1` mixed duplicate CI work, optional browser/runtime/recovery modes, and release evidence into one command. It is removed in favor of the explicit workflows and focused scripts above. The certification result artifact contains the candidate SHA, source CI identity, same-SHA security conclusions, every suite conclusion and duration, image publication result, and workflow URL. Evidence artifacts are retained for 30 days; the release manifest is retained for 90 days for the downstream CD handoff.

## Suite placement criteria

- **PR CI:** deterministic build/static checks and focused behavior checks fast enough to run on each pull request. The existing required CI job set is intentionally unchanged.
- **Post-merge release certification:** environment-dependent runtime/deployment safety, complete browser business workflows, production configuration checks, and backup/restore recovery. Each suite answers a different release-risk question and must pass for the same candidate SHA.
- **Nightly/manual certification:** the same reusable suites and evidence shape, but `release_run=false`; it cannot publish images or deploy. A manually supplied SHA must resolve to a commit reachable from `main`.
- **Release image publication:** only after all release suites pass; tags are the exact full candidate SHA. Publication failure fails the certification manifest and blocks CD.
- **Production CD:** downloads the manifest from the triggering certification run, verifies the manifest belongs to that run, independently looks up its source CI run through GitHub, requires a successful same-repository push to `main` for the same SHA, verifies all suite/image results, then pulls digest-pinned images and confirms their image IDs match the tested bundle before deployment.

## Validation and coverage-preservation checks

Before approving workflow changes, confirm the unchanged `CI` workflow still exposes its required job set; all files under `e2e/*.spec.js` remain discoverable through the `npm run e2e` command; runtime and recovery scripts retain their distinct non-browser checks; and failed, skipped, cancelled, wrong-repository, stale-SHA, or missing-manifest cases cannot publish or deploy. The artifact manifest is authoritative for the certification SHA and suite result, not the nightly job's current default branch tip.
