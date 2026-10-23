#!/usr/bin/env python3
"""Vendor (or verify) the two pinned sources that R002 must not re-author.

* The Causa domain engine: kemiller2002/indy-init-rehearsal-001 @ 55f49f61 (the semantic oracle named by
  causa-data engine.lock.json and REHEARSAL-INTERFACE-MANIFEST-R001.md).
* The Causa storage contract reader/writer and schemas: kemiller2002/causa-data @ tag causa-storage-v1.0.0.

Files are copied byte-for-byte and identified by their Git blob SHA. `verify` recomputes the blob SHA of every
vendored file and fails on any difference, so a local edit of a vendored file cannot pass CI.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / "src" / "VENDOR.json"

SOURCES = (
    {"id": "causa-domain-engine", "repository": "kemiller2002/indy-init-rehearsal-001",
     "ref": "55f49f61c939a3a780cee592f36f732a1328f981",
     "files": [("src/engine/" + f + ".fs", "src/domain/" + f + ".fs") for f in ("Primitives", "Model", "Commands", "Decide", "Codec")]},
    {"id": "causa-storage-contract", "repository": "kemiller2002/causa-data", "ref": "causa-storage-v1.0.0",
     "files": [("tools/src/Causa.Contract/" + f + ".fs", "src/contract/" + f + ".fs")
               for f in ("Json", "Errors", "Wire", "Events", "Projection", "SchemaCheck", "Store")]},
)


def git_blob_sha(data: bytes) -> str:
    return hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()


def git(repo: Path, *args: str) -> bytes:
    return subprocess.run(["git", "-C", str(repo), *args], check=True, capture_output=True).stdout


def schema_files(checkout: Path, ref: str) -> list[tuple[str, str]]:
    listing = git(checkout, "ls-tree", "-r", "--name-only", ref, "contract/v1.0.0/schemas").decode().splitlines()
    return [(p, "src/contract/schemas/" + p[len("contract/v1.0.0/schemas/"):]) for p in listing]


def entries(source: dict, checkout: Path) -> list[dict]:
    pairs = source["files"] + (schema_files(checkout, source["ref"]) if source["id"] == "causa-storage-contract" else [])
    commit = git(checkout, "rev-parse", source["ref"] + "^{commit}").decode().strip()

    def entry(pair: tuple[str, str]) -> dict:
        upstream, local = pair
        data = git(checkout, "show", f"{source['ref']}:{upstream}")
        target = ROOT / local
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        return {"upstream": upstream, "path": local, "blob": git_blob_sha(data),
                "sha256": hashlib.sha256(data).hexdigest()}

    return [{"commit": commit, **entry(p)} for p in pairs]


def vendor(engine: Path, data: Path) -> None:
    checkouts = {"causa-domain-engine": engine, "causa-storage-contract": data}
    manifest = {
        "schema": "causa-r002.vendor/1",
        "note": "Byte-identical copies. Do not edit; re-run tools/vendor/vendor.py from the pinned sources.",
        "sources": [{"id": s["id"], "repository": s["repository"], "ref": s["ref"],
                     "files": [{k: v for k, v in e.items() if k != "commit"} for e in es],
                     "commit": es[0]["commit"]}
                    for s in SOURCES for es in [entries(s, checkouts[s["id"]])]],
    }
    MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


def verify() -> int:
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    problems = [f"{f['path']}: blob {git_blob_sha((ROOT / f['path']).read_bytes()) if (ROOT / f['path']).exists() else 'missing'} != {f['blob']}"
                for s in manifest["sources"] for f in s["files"]
                if not (ROOT / f["path"]).exists() or git_blob_sha((ROOT / f["path"]).read_bytes()) != f["blob"]]
    count = sum(len(s["files"]) for s in manifest["sources"])
    print("\n".join(problems) if problems else f"vendor: {count} files byte-identical to their pinned sources")
    return 1 if problems else 0


def main() -> int:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    v = sub.add_parser("vendor")
    v.add_argument("--engine-checkout", type=Path, required=True)
    v.add_argument("--data-checkout", type=Path, required=True)
    sub.add_parser("verify")
    args = parser.parse_args()
    if args.command == "vendor":
        vendor(args.engine_checkout, args.data_checkout)
    return verify()


if __name__ == "__main__":
    sys.exit(main())
