#!/usr/bin/env python3
"""Generate and validate the rehearsal acceptance ledger (docs/requirements/REHEARSAL-ACCEPTANCE-LEDGER.md).

Inputs (all version-controlled):
  docs/requirements/*.md              requirement ids, titles and priorities (the pinned requirements authority)
  evidence/acceptance/trace.json      per-requirement trace: tests, evidence, status, reason (hand-maintained)
  evidence/acceptance/run.json        run identity, gates, defect classes, metrics, unresolved decisions
Output:
  evidence/acceptance/ledger.json

Requirements without a trace are NOT_RUN, never PASS. `validate` rejects duplicate ids, unknown statuses, PASS
without proof, mismatched SHAs and an accepted flag with incomplete gates.

Usage: ledger.py generate | ledger.py validate
"""
import hashlib
import json
import re
import sys
from functools import reduce
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
REQ_DIR = ROOT / "docs" / "requirements"
ACCEPTANCE = ROOT / "evidence" / "acceptance"
STATUSES = frozenset({"NOT_RUN", "PASS", "FAIL", "BLOCKED"})
REQUIRED_GATES = tuple(f"G{i}" for i in range(10))
FAMILIES = ("UI", "EXT-DATA", "WASM-LOAD", "DCC", "ADRP", "DEP-CURRENT")
HEADING = re.compile(r"^#{2,3}\s+(?P<id>(?:%s)-\d{3}):\s*(?P<title>.*?)\s*(?:\[(?P<p>P\d)\])?\s*$" % "|".join(FAMILIES))
BULLET = re.compile(r"^-\s+(?P<id>(?:%s)-\d{3})\s+\[(?P<p>P\d)\]\s+(?P<title>.*)$" % "|".join(FAMILIES))


def read_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def requirement_of(doc: str, line: str):
    m = HEADING.match(line) or BULLET.match(line)
    return None if m is None else {
        "id": m["id"], "source_doc": f"docs/requirements/{doc}", "priority": m["p"] or "P0",
        "title": m["title"].strip()[:160],
    }


def requirements_in(path: Path):
    return [r for r in (requirement_of(path.name, line) for line in path.read_text(encoding="utf-8").splitlines()) if r]


def discovered_requirements():
    found = [r for doc in sorted(REQ_DIR.glob("*.md")) for r in requirements_in(doc)]
    # First definition wins (a later document may cite an id it does not define).
    return list(reduce(lambda acc, r: acc if r["id"] in acc else {**acc, r["id"]: r}, found, {}).values())


def traced(requirement, trace):
    t = trace.get(requirement["id"])
    untraced = {"owner": None, "implementation_paths": [], "tests": [], "evidence_paths": [], "status": "NOT_RUN",
                "reason": "not traced to tests or evidence in this run"}
    return {**requirement, **(untraced if t is None else {
        "owner": t.get("owner"), "implementation_paths": t.get("implementation_paths", []), "tests": t.get("tests", []),
        "evidence_paths": t.get("evidence_paths", []), "status": t["status"], "reason": t.get("reason", "")})}


def generate():
    run = read_json(ACCEPTANCE / "run.json")
    trace = read_json(ACCEPTANCE / "trace.json")
    requirements = [traced(r, trace) for r in discovered_requirements()]
    unknown = sorted(set(trace) - {r["id"] for r in requirements})
    if unknown:
        raise SystemExit(f"trace names requirements no document defines: {unknown}")
    gates = run["gates"]
    accepted = all(g["status"] == "PASS" and g["verified_sha"] == run["source_commit_sha"] for g in gates if g["id"] in REQUIRED_GATES) \
        and not any(d["severity"] == "critical" and d["status"] != "CLOSED" for d in run["defect_classes"]) \
        and run.get("deployed_commit_sha") == run["source_commit_sha"]
    ledger = {
        "run_id": run["run_id"],
        "source_commit_sha": run["source_commit_sha"],
        "deployed_commit_sha": run.get("deployed_commit_sha"),
        "contract_version": run["contract_version"],
        "frozen_fixture_digest": run["frozen_fixture_digest"],
        "dependency_lock_sha": sha256(ROOT / "evidence" / "stack" / "stack-lock.json"),
        "interface_manifest_sha": sha256(ROOT / "docs" / "architecture" / "VIEW-CONTRACT.md"),
        "requirements": requirements,
        "defect_classes": run["defect_classes"],
        "gates": gates,
        "metrics": run["metrics"],
        "unresolved_decisions": run["unresolved_decisions"],
        "summary": {s: sum(1 for r in requirements if r["status"] == s) for s in sorted(STATUSES)},
        "accepted": accepted,
    }
    (ACCEPTANCE / "ledger.json").write_text(json.dumps(ledger, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return ledger


def problems(ledger):
    ids = [r["id"] for r in ledger["requirements"]]
    gates = {g["id"]: g for g in ledger["gates"]}
    checks = [
        ([f"duplicate requirement id {i}" for i in sorted({i for i in ids if ids.count(i) > 1})]),
        ([f"{r['id']}: unknown status {r['status']}" for r in ledger["requirements"] if r["status"] not in STATUSES]),
        ([f"{r['id']}: PASS without tests or evidence" for r in ledger["requirements"]
          if r["status"] == "PASS" and not (r["tests"] or r["evidence_paths"])]),
        ([f"{r['id']}: {r['status']} without a reason" for r in ledger["requirements"]
          if r["status"] in ("FAIL", "BLOCKED", "NOT_RUN") and not r["reason"]]),
        ([f"gate {g['id']}: unknown status {g['status']}" for g in ledger["gates"] if g["status"] not in STATUSES]),
        ([f"gate {g['id']}: PASS without evidence" for g in ledger["gates"] if g["status"] == "PASS" and not g["evidence_paths"]]),
        ([f"gate {g['id']}: PASS on {g['verified_sha']}, not the source commit" for g in ledger["gates"]
          if g["status"] == "PASS" and g["id"] in REQUIRED_GATES and g["verified_sha"] != ledger["source_commit_sha"]]),
        ([f"missing gate {g}" for g in REQUIRED_GATES if g not in gates]),
        (["accepted while a required gate is not PASS"] if ledger["accepted"] and any(
            gates.get(g, {}).get("status") != "PASS" for g in REQUIRED_GATES) else []),
        (["accepted with a deployed SHA that differs from the source"] if ledger["accepted"] and
         ledger["deployed_commit_sha"] != ledger["source_commit_sha"] else []),
        ([f"metric {k} is not a number or null" for k, v in ledger["metrics"].items()
          if not (v is None or isinstance(v, (int, float)) or isinstance(v, dict))]),
        ([f"evidence path missing: {p}" for p in sorted({p for r in ledger["requirements"] for p in r["evidence_paths"]}
                                                         | {p for g in ledger["gates"] for p in g["evidence_paths"]})
          if not p.startswith("http") and not (ROOT / p).exists()]),
    ]
    return [p for group in checks for p in group]


def main(argv):
    command = argv[1] if len(argv) > 1 else "generate"
    ledger = generate() if command == "generate" else read_json(ACCEPTANCE / "ledger.json")
    found = problems(ledger)
    print("\n".join(found) if found else
          f"ledger: {len(ledger['requirements'])} requirements {ledger['summary']}; accepted={ledger['accepted']}")
    return 1 if found else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
