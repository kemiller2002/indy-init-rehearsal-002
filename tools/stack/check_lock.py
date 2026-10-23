#!/usr/bin/env python3
"""DEP-CURRENT-008 enforcement: fail when what the repository actually installs differs from the frozen run lock
(evidence/stack/stack-lock.json), or when required identity/verification fields are missing.

Checks (all pure reads): package-lock.json resolved versions and integrity for the Echelon web packages and browser
tooling; NuGet PackageReference versions; vendor/nuget limen-fsharp lock digests; conditor.json component versions;
global.json SDK; vendored domain/contract identities (src/VENDOR.json). Exit 1 on any mismatch.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def load(rel: str):
    return json.loads((ROOT / rel).read_text(encoding="utf-8"))


def npm_version(lock: dict, name: str):
    entry = lock.get("packages", {}).get(f"node_modules/{name}", {})
    return entry.get("version"), entry.get("resolved"), entry.get("integrity")


def nuget_refs() -> dict[str, set[str]]:
    pattern = re.compile(r'<PackageReference Include="([^"]+)" Version="([^"]+)"')
    refs: dict[str, set[str]] = {}
    for proj in list(ROOT.glob("src/**/*.fsproj")) + list(ROOT.glob("src/**/*.csproj")) + list(ROOT.glob("tests/**/*.fsproj")):
        for name, version in pattern.findall(proj.read_text(encoding="utf-8")):
            refs.setdefault(name, set()).add(version)
    return refs


def problems(lock: dict) -> list[str]:
    npm = load("package-lock.json")
    conditor = {c["id"]: c["version"] for c in load("conditor.json")["components"]}
    sdk = load("global.json")["sdk"]["version"]
    refs = nuget_refs()
    vendor = load("src/VENDOR.json")
    out: list[str] = []

    for c in lock["components"]:
        for field in ("systemId", "selectedVersion", "sourceCommit", "verification"):
            if not c.get(field):
                out.append(f"{c.get('systemId')}: missing {field}")
        if c.get("applicability") != "APPLICABLE":
            continue
        for binding in c.get("bindings", []):
            kind = binding["kind"]
            if kind == "npm":
                version, resolved, integrity = npm_version(npm, binding["package"])
                if version != binding["version"]:
                    out.append(f"{binding['package']}: package-lock has {version}, lock requires {binding['version']}")
                if binding.get("integrity") and integrity != binding["integrity"]:
                    out.append(f"{binding['package']}: integrity {integrity} != {binding['integrity']}")
                if binding.get("resolved") and resolved != binding["resolved"]:
                    out.append(f"{binding['package']}: resolved {resolved} != {binding['resolved']}")
            elif kind == "nuget":
                found = refs.get(binding["package"], set())
                if found != {binding["version"]}:
                    out.append(f"{binding['package']}: project references {sorted(found)}, lock requires {binding['version']}")
            elif kind == "conditor":
                if conditor.get(binding["id"]) != binding["version"]:
                    out.append(f"conditor.json {binding['id']}: {conditor.get(binding['id'])} != {binding['version']}")
            elif kind == "nuget-feed":
                lines = (ROOT / "vendor/nuget/limen-fsharp.lock").read_text(encoding="utf-8").splitlines()
                digests = {parts[1]: parts[4].removeprefix("sha256:") for parts in (l.split() for l in lines) if parts and parts[0] == "package"}
                for pid, digest in binding["packages"].items():
                    if digests.get(pid) != digest:
                        out.append(f"vendor/nuget {pid}: {digests.get(pid)} != {digest}")
            elif kind == "sdk":
                if sdk != binding["version"]:
                    out.append(f"global.json SDK {sdk} != {binding['version']}")
            elif kind == "vendor":
                source = next((s for s in vendor["sources"] if s["id"] == binding["id"]), None)
                if source is None or source["commit"] != binding["commit"]:
                    out.append(f"src/VENDOR.json {binding['id']}: commit {source and source['commit']} != {binding['commit']}")
    return out


def main() -> int:
    lock = load("evidence/stack/stack-lock.json")
    if lock.get("status") != "FROZEN":
        print(f"stack lock status is {lock.get('status')}, not FROZEN")
        return 1
    found = problems(lock)
    print("\n".join(found) if found else f"stack lock: {len(lock['components'])} components match the frozen run lock")
    return 1 if found else 0


if __name__ == "__main__":
    sys.exit(main())
