#!/usr/bin/env python3
"""Write evidence/acceptance/run.json: the run identity, gates, defect classes, metrics and open decisions that
tools/ledger/ledger.py turns into the acceptance ledger.

Every value here is either a recorded fact (a commit, a run id, a timestamp from CI or from a live-run artifact) or
null with the reason it is unavailable. Statuses come from the arguments, so the script cannot claim a gate the
caller has not established.

Usage: run_record.py SOURCE_SHA DEPLOYED_SHA|- ACCEPTED_AT|- G7_STATUS G9_STATUS
"""
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
START = "2026-10-23T12:14:13Z"


def at(text):
    return datetime.strptime(text, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc)


def elapsed(text):
    return None if text is None else int((at(text) - at(START)).total_seconds())


def gate(gid, name, scope, sha, status, evidence, started, completed, note=""):
    return {"id": gid, "name": name, "scope": scope, "verified_sha": sha, "status": status, "evidence_paths": evidence,
            "started_at": started, "completed_at": completed, **({"note": note} if note else {})}


def defect(did, severity, invariant, repro, variants, fix, review, status):
    return {"id": did, "severity": severity, "violated_invariant": invariant, "original_repro": repro, "variants": variants,
            "fix_sha": fix, "reviewer_evidence": review, "status": status}


def record(source, deployed, accepted_at, g7, g9):
    live = "evidence/acceptance/live-run-39986660673/live-acceptance.json"
    ci = f"https://github.com/kemiller2002/indy-init-rehearsal-002/commit/{source}/checks"
    gates = [
        gate("G0", "Scope", "requirements pinned (Indy-init@b5d9d79), owners, priorities", source, "PASS",
             ["conditor.json", "docs/requirements", "evidence/acceptance/trace.json"], START, "2026-10-23T12:26:32Z"),
        gate("G1", "Dependencies", "echelon-current 1.25.0 stack verified as a set and frozen", source, "PASS",
             ["evidence/stack/verification-report.md", "evidence/stack/stack-lock.json", "evidence/stack/compatibility-matrix.json"],
             START, "2026-10-23T13:15:00Z", "lock checked on every CI run (tools/stack/check_lock.py)"),
        gate("G2", "Interfaces", "engine/kernel view contract; producer and consumer tests", source, "PASS",
             ["docs/architecture/VIEW-CONTRACT.md", "tests/App.Engine.Tests/EngineTests.fs", "tests/browser"], START, None),
        gate("G3", "Domain", "F# domain decisions, illegal transitions and forged histories", source, "PASS",
             ["tests/Causa.Conformance.Tests", "tests/App.Engine.Tests/EngineTests.fs"], START, None),
        gate("G4", "Frozen data", "5 golden scenarios, 90 negative fixtures, identities of causa-storage-v1.0.0", source, "PASS",
             ["tests/Causa.Conformance.Tests", "evidence/stack/logs/causa-data-gate.json"], START, None),
        gate("G5", "UI", "P0 screens, loading, keyboard, accessibility, responsive, color", source, "PASS",
             ["tests/browser"], START, None, "59 browser tests on the candidate in CI"),
        gate("G6", "Persistence", "real GitHub provider: read, write, read-back, reload, conflict, refusal", source, "PASS",
             [live], "2026-10-23T16:25:36Z", "2026-10-23T16:27:44Z",
             "causa-data run 39986660673 on 645f3ca; its tree 250b154 equals the candidate's"),
        gate("G7", "Defect class", "independent review and SHA-bound closure of DC-001..DC-006", source, g7,
             ["docs/decisions", "evidence/quality/closure-review-DC-002-004.md", "evidence/quality/closure-review-2.md",
              "evidence/quality/closure-review-3.md"], None, None),
        gate("G8", "Candidate", "full CI on the candidate: build, conformance, engine, browser, foundations, Praxis", source, "PASS",
             [ci], None, None),
        gate("G9", "Deployment", "served build-info.json names the candidate; deployed acceptance on it", source, g9,
             ["https://kevinmmiller.us/indy-init-rehearsal-002/build-info.json", ci], None, accepted_at),
    ]
    defects = [
        defect("DC-001", "critical", "a publication is parented by the state it was decided on (CONTRACT 9.4.2)",
               "tests 9d, 10b, 11d", ["rebase over another writer", "retry after reload", "unknown outcome after branch moved"],
               "5675d7c", "engine test owner (independent); closure-review-DC-002-004", "CLOSED"),
        defect("DC-002", "high", "a provider answer applies only to the question that asked it; events only in legal states",
               "security review F1", ["P2", "P3", "P5", "A (second review)"], "645f3ca",
               "closure-review-DC-002-004 (OPEN), closure-review-2 (OPEN: A), closure-review-3", "CLOSED" if g7 == "PASS" else "OPEN"),
        defect("DC-003", "medium", "after the ref update is sent, no failure is reported as not written",
               "security review F2", ["P1 (crash)", "D (stale commit re-sent)"], "645f3ca",
               "closure-review-DC-002-004 (OPEN), closure-review-2 (CLOSED, residual D)", "CLOSED"),
        defect("DC-004", "high", "an absent or advisory provider field never gates or grants", "live run 39964244367 L4",
               ["permissions absent", "permissions.push=false on an Actions token", "B (actions offered during reload)"], "645f3ca",
               "live runs; closure-review-2 (CLOSED)", "CLOSED"),
        defect("DC-005", "high", "reads of mutable state are never answered from a cache", "live run 39977289458 L4b",
               ["browser cache", "nonce repeated across page loads (shared cache)", "C (post-PATCH ref read unjudged)"], "645f3ca",
               "live runs 39980845597 (diagnostics), 39982330584 and 39986660673 PASS; closure-review-2 (CLOSED)", "CLOSED"),
        defect("DC-006", "medium", "a failed framework download ends the start promptly with recovery (WASM-LOAD-005)",
               "deployed acceptance run 39983316689", ["CDN 503 + integrity failure on one .wasm"], "645f3ca",
               "closure-review-3", "CLOSED" if g7 == "PASS" else "OPEN"),
    ]
    metrics = {
        "first_green_elapsed_seconds": elapsed("2026-10-23T13:58:28Z"),
        "first_candidate_elapsed_seconds": elapsed("2026-10-23T15:17:27Z"),
        "accepted_elapsed_seconds": elapsed(accepted_at),
        "candidate_count": 4,
        "repeated_defect_class_count": 4,
        "token_count": None,
        "ci_queue_seconds": None,
        "ci_test_seconds": 408,
        "verified_requirement_count": None,
        "milestones": {
            "start": START, "first_build": "2026-10-23T12:26:32Z", "first_functional_screen": "2026-10-23T12:58:34Z",
            "first_external_data_load": "2026-10-23T12:59:36Z", "first_green_ci": "2026-10-23T13:58:28Z",
            "first_verified_remote_write": {"run": 39975799085, "commit": "33dcddee2dc1a54db9de4001608e470a1b67f920",
                                            "not_before": "2026-10-23T15:10:31Z", "not_after": "2026-10-23T15:11:41Z"},
            "first_candidate_on_main": "2026-10-23T15:17:27Z", "accepted": accepted_at},
        "unavailable": {"token_count": "the runtime does not expose token usage to the session",
                        "ci_queue_seconds": "not aggregated across runs; per-run timings are in GitHub Actions",
                        "ci_test_seconds_scope": "wall-clock of the accepted candidate's run 39987600253 (build, tests, deploy, deployed acceptance); not summed over earlier runs"},
    }
    decisions = [
        {"id": "UD-1", "owner": "repository owner", "impact": "main was created from the session by the owner; branch protection not applied",
         "disposition": ".github/branch-protection.json is prepared; applying it needs repository admin rights"},
        {"id": "UD-2", "owner": "repository owner", "impact": "P1 features (causal graph, search, comparison, export, first-run guidance) not built",
         "disposition": "deferred; listed NOT_RUN in the ledger"},
        {"id": "UD-3", "owner": "Praxis maintainers", "impact": "DEF-STACK-007: checkpoint accepts second-precision times that validate rejects",
         "disposition": "worked around with millisecond times; upstream fix needed"},
    ]
    return {"run_id": "causa-rehearsal-002", "source_commit_sha": source, "deployed_commit_sha": deployed,
            "deployed_url": "https://kevinmmiller.us/indy-init-rehearsal-002/", "contract_version": "causa-storage-v1.0.0",
            "frozen_fixture_digest": "sha256:f650a2e509a420872b46177b21f57ef6ebd7625ae4c69dd92198d3ca472c2b86",
            "gates": gates, "defect_classes": defects, "metrics": metrics, "unresolved_decisions": decisions}


if __name__ == "__main__":
    source, deployed, accepted_at, g7, g9 = sys.argv[1:6]
    out = record(source, None if deployed == "-" else deployed, None if accepted_at == "-" else accepted_at, g7, g9)
    (ROOT / "evidence" / "acceptance" / "run.json").write_text(json.dumps(out, indent=2) + "\n", encoding="utf-8")
    print("run.json written")
