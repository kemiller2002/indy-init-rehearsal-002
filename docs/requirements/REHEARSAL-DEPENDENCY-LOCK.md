# Current-Echelon-Stack Verification Policy

Status: normative. Supersedes the prior R001-retention defaults in this document and any conflicting version-retention language in AGENT-DECISION-REDUCTION-PROFILE.md. R001 versions are historical baselines only, not targets for future experiments.

## DEP-CURRENT-001: Discover every run
Before every rehearsal, competition run, or new application build, inventory every required Echelon dependency and toolchain component from actual manifests, registries, release artifacts, and source repositories. This includes Forma, Forma foundations, Limen, Folio, Fides when used, Arca when used, Strata when used, Ordo, Praxis, Dokimos, Percepta, Aegis, Conditor, routing, .NET/F#, JavaScript/TypeScript, browser test tools, and transitive components that materially affect compatibility. A system not in scope must be marked NOT_APPLICABLE, not assumed installed.

## DEP-CURRENT-002: Prefer latest approved stable
Select the newest approved stable release for each required component as the candidate. A newer compatible source commit may be used instead only with explicit source SHA, build provenance, and documented approval. Do not automatically select unstable previews or unreviewed main-branch commits merely because they are newer. Do not silently retain R001 versions.

## DEP-CURRENT-003: Resolve the stack as a set
Check cross-component dependency constraints, shared contract versions, WASM boundary compatibility, .NET SDK/toolchain, Forma foundations and component compatibility, Folio integration, and auth/storage adapter contracts. Never select independent latest versions without resolving the whole graph.

## DEP-CURRENT-004: Verify before lock
For the candidate stack, run clean restore/install, F# build/tests, Limen startup, Forma rendering and accessibility, Folio printing, frozen-data golden replay, persistence conflict/remote-acknowledgment tests where applicable, and exact-deployment acceptance. Collect machine-readable outcomes, commands, source SHAs, artifact digests, and logs. Do not label a candidate VERIFIED based solely on published version metadata.

## DEP-CURRENT-005: Block on incompatibility
If the latest candidate is incompatible, open a defect-class record identifying producer, consumer, contract and reproduction. Fix the integration or obtain an explicit exception. A fallback to an older release is never automatic, and must record its scope, rationale, owner, expiry/revisit trigger, and effect on experimental conclusions. Never conceal a downgrade.

## DEP-CURRENT-006: Freeze at kickoff, refresh next run
After all applicable verification gates pass, record a complete immutable lock manifest for that run: dependency, selected version, source URL, commit SHA, integrity checksum, discovery timestamp, verification SHA and test evidence. The run uses exactly this set. New upstream releases during the run are tracked as drift but do not automatically alter an in-progress experiment. Repeat discovery and verification for the next run.

## DEP-CURRENT-007: Avoid invalid experiments
A timed experiment must compare a known, fully verified platform baseline. If current releases cannot be verified, report BLOCKED and separate framework defects from application-agent outcomes. Do not claim performance or correctness improvements attributable to a run with uncontrolled dependency drift.

## DEP-CURRENT-008: Enforcement
Add an automated dependency-discovery job and a compatibility matrix gate before agent fan-out. Fail CI if actual installed versions differ from the run lock, or if required version, SHA, provenance, or verification evidence is missing. Store a diff against the previous run so updates are explicit.

## Current observed historical baseline (NOT a recommended target)
R001 package-lock.json listed Forma 0.5.0, Limen 0.9.0, Folio v0.3.0 release tarball, TypeScript 5.9.3, Playwright 1.63.0, and axe-core Playwright 4.13.0. R001 .ros/installation.json recorded Praxis 3.16.0. These observations do not establish today's latest releases or compatibility.

## Preflight acceptance
1. Discover the newest approved stable versions from actual release sources.
2. Resolve the dependency graph, including Forma foundations and Echelon integrations.
3. Execute compatibility and security gates.
4. Produce a versioned stack manifest and verification report.
5. Freeze only after PASS; otherwise mark BLOCKED and investigate.
6. Re-run discovery at the next kickoff.

No live registry discovery or build verification is claimed by this document.
