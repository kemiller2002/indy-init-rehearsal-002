# Rehearsal Executable Acceptance Ledger Contract

Status: normative specification for a generated machine-readable ledger. **No current test results are asserted.**

## Single ledger format
Generate a version-controlled JSON document with:
- run_id, source_commit_sha, deployed_commit_sha (nullable until deployed), contract_version, frozen_fixture_digest, dependency_lock_sha, interface_manifest_sha
- requirements[]: {id, source_doc, priority, owner, implementation_paths[], tests[], evidence_paths[], status, reason}
- defect_classes[]: {id, severity, violated_invariant, original_repro, variants[], fix_sha, reviewer_evidence, status}
- gates[]: {id, name, scope, verified_sha, status, evidence_paths[], started_at, completed_at}
- metrics: {first_green_elapsed_seconds, first_candidate_elapsed_seconds, accepted_elapsed_seconds, candidate_count, repeated_defect_class_count, token_count, ci_queue_seconds, ci_test_seconds, verified_requirement_count}
- unresolved_decisions[] with owner, impact, and disposition.

Allowed statuses: NOT_RUN, PASS, FAIL, BLOCKED. Never convert missing evidence to PASS. No synthetic metrics.

## Gate matrix
| Gate | Mandatory evidence | Failure behavior |
|---|---|---|
| G0 Scope | requirement IDs, owner, profile priority, source precedence resolution | BLOCKED if mandatory conflict |
| G1 Dependencies | verified package/SHA/integrity lock, SDK, WASM/Forma/Folio smoke | BLOCKED if unknown |
| G2 Interfaces | frozen versioned handoff manifest and producer/consumer tests | BLOCKED if not extracted |
| G3 Domain | F# unit tests, illegal-transition and forged-history variants | FAIL if invalid history accepted |
| G4 Frozen data | five golden scenarios, hashes, 90 negative fixtures where applicable | FAIL on schema drift |
| G5 UI | P0 screen contracts, startup/error, iPad/keyboard/a11y, projection fidelity | FAIL for missing required behavior |
| G6 Persistence | local vs remote truth, expected-version conflict, reload, no fixture mutation | BLOCKED if live provider untested |
| G7 Defect class | independent review and SHA-bound closure for critical/recurring classes | FAIL while open |
| G8 Candidate | full applicable browser/integration/security/report tests | FAIL on missing required suite |
| G9 Deployment | deployed SHA equals verified SHA, successful acceptance job on same SHA | FAIL/BLOCKED otherwise |
| G10 Measurement | elapsed time, candidates, review recurrence, token and test cost | BLOCKED if unavailable; do not invent |

## Release acceptance predicate
Accepted is permitted only if all required gates G0–G9 are PASS for the same verified source identity, no open critical defect class remains, and deployed acceptance is PASS. G10 completeness is separately reported and cannot be silently assumed. The run may be incomplete, blocked, or failed; partial successes are reported honestly.

## Requirement coverage
Map the normative requirements in REQUIREMENTS.md, CAUSA-UI-EXPERIENCE-REQUIREMENTS.md, WASM-LOADING-EXPERIENCE-REQUIREMENTS.md, FROZEN-DEMO-STORAGE-REQUIREMENTS.md, DEFECT-CLASS-CLOSURE-AND-RELEASE-GATES.md and AGENT-DECISION-REDUCTION-PROFILE.md. Map existing SCREEN-CONTRACTS.md where screens are implemented. P1 scope is tracked explicitly; deferred items remain NOT_RUN rather than disappearing.

## Release batching and evidence
Use targeted checks during development; batch compatible fixes into a release candidate. Independent architecture review before first candidate. Preserve R001 baseline and historical results unchanged. Emit one ledger per candidate and one final immutable report with exact source/deployment identities.

## Next implementation step
Implement a small validator/generator consuming requirement IDs, test results and gate artifacts. It must reject duplicate IDs, unknown statuses, missing proof, mismatched SHAs, and an Accepted flag with incomplete required gates. Do not build a new generic workflow platform merely to implement this ledger.
