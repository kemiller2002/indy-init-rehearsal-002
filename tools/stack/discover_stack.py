#!/usr/bin/env python3
"""Echelon stack discovery for Causa Rehearsal 002 (DEP-CURRENT-001..008).

Discovery is NOT verification. This tool records, per component:
  * the approval source (echelon-registry `echelon-current` channel, resolved set for linux-x64),
  * the newest available release found at the actual release source,
  * the newest approved stable release (the channel's selection),
  * the tag -> commit resolution observed with `git ls-remote` against the registry's recorded commit,
  * the SHA-256 of every downloaded artifact compared with the registry's release manifest.

Pure functions build records; the only effects are `fetch_json`, `fetch_bytes` and `ls_remote`, injected into `main`.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import urllib.error
import urllib.request
from dataclasses import dataclass
from datetime import datetime, timezone
from functools import reduce
from pathlib import Path
from typing import Callable, Iterable, Mapping, Optional

USER_AGENT = "causa-r002-stack-discovery/1"
SEMVER = re.compile(r"(\d+)\.(\d+)\.(\d+)$")


# ---------------------------------------------------------------------------------------------------------------------
# Effects (the only impure functions)
# ---------------------------------------------------------------------------------------------------------------------

def fetch_json(url: str) -> object:
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept": "application/vnd.github+json"})
    with urllib.request.urlopen(req, timeout=60) as response:
        return json.load(response)


def fetch_bytes(url: str) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=300) as response:
        return response.read()


def ls_remote(repository: str) -> str:
    return subprocess.run(
        ["git", "ls-remote", "--tags", f"https://github.com/{repository}"],
        check=True, capture_output=True, text=True, timeout=120).stdout


# ---------------------------------------------------------------------------------------------------------------------
# Pure helpers
# ---------------------------------------------------------------------------------------------------------------------

def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def semver_key(tag: str) -> Optional[tuple[int, int, int]]:
    match = SEMVER.search(tag)
    return tuple(int(x) for x in match.groups()) if match else None  # type: ignore[return-value]


def parse_ls_remote(text: str) -> Mapping[str, str]:
    """tag name -> peeled commit SHA (annotated tags resolve through their ^{} line)."""
    rows = [line.split("\t") for line in text.splitlines() if "\t" in line]
    direct = {ref[len("refs/tags/"):]: sha for sha, ref in rows if not ref.endswith("^{}")}
    peeled = {ref[len("refs/tags/"):-3]: sha for sha, ref in rows if ref.endswith("^{}")}
    return {**direct, **peeled}


def tag_prefix(approved_tag: str) -> str:
    """The tag family of a component (e.g. 'dokimos-v' for 'dokimos-v0.6.0', 'v' for 'v0.9.0')."""
    match = SEMVER.search(approved_tag)
    return approved_tag[: match.start()] if match else approved_tag


def newest_tag(tags: Iterable[str], prefix: str) -> Optional[str]:
    candidates = [t for t in tags if t.startswith(prefix) and "-" not in t[len(prefix):] and semver_key(t)]
    return max(candidates, key=lambda t: semver_key(t)) if candidates else None  # type: ignore[arg-type,return-value]


def newest_stable_release(releases: object, prefix: str) -> Optional[Mapping[str, object]]:
    if not isinstance(releases, list):
        return None
    stable = [r for r in releases
              if isinstance(r, dict) and not r.get("draft") and not r.get("prerelease")
              and str(r.get("tag_name", "")).startswith(prefix) and semver_key(str(r["tag_name"]))]
    return max(stable, key=lambda r: semver_key(str(r["tag_name"]))) if stable else None  # type: ignore[arg-type]


def asset_url(component: Mapping[str, object], artifact_name: str) -> Optional[str]:
    dist = component["distribution"]  # type: ignore[index]
    mechanism = dist["mechanism"]  # type: ignore[index]
    if mechanism in ("npm", "nuget"):
        return str(dist["url"])  # type: ignore[index]
    if mechanism == "github-release":
        return f"https://github.com/{component['repository']}/releases/download/{component['tag']}/{artifact_name}"
    return None


# ---------------------------------------------------------------------------------------------------------------------
# Record construction
# ---------------------------------------------------------------------------------------------------------------------

@dataclass(frozen=True)
class Effects:
    fetch_json: Callable[[str], object]
    fetch_bytes: Callable[[str], bytes]
    ls_remote: Callable[[str], str]


def safe(thunk: Callable[[], object]) -> tuple[Optional[object], Optional[str]]:
    try:
        return thunk(), None
    except (urllib.error.URLError, OSError, subprocess.SubprocessError, ValueError, KeyError) as ex:
        return None, f"{type(ex).__name__}: {ex}"


def npm_latest(package: str, effects: Effects) -> Mapping[str, object]:
    data, error = safe(lambda: effects.fetch_json("https://registry.npmjs.org/" + package.replace("/", "%2f")))
    if error or not isinstance(data, dict):
        return {"source": "npm", "error": error}
    latest = data.get("dist-tags", {}).get("latest")
    dist = data.get("versions", {}).get(latest, {}).get("dist", {})
    return {"source": "npm", "latestDistTag": latest, "integrity": dist.get("integrity"), "tarball": dist.get("tarball")}


def nuget_versions(package: str, effects: Effects) -> Mapping[str, object]:
    data, error = safe(lambda: effects.fetch_json(f"https://api.nuget.org/v3-flatcontainer/{package.lower()}/index.json"))
    if error or not isinstance(data, dict):
        return {"source": "nuget.org", "error": error}
    versions = [v for v in data.get("versions", []) if "-" not in v]
    return {"source": "nuget.org", "newestStable": max(versions, key=lambda v: semver_key(v) or (0, 0, 0)) if versions else None}


def discover_component(component: Mapping[str, object], effects: Effects, download: bool) -> Mapping[str, object]:
    repository = str(component["repository"])
    approved_tag = str(component["tag"])
    prefix = tag_prefix(approved_tag)

    tags_text, tags_error = safe(lambda: effects.ls_remote(repository))
    tags = parse_ls_remote(str(tags_text)) if tags_text else {}
    releases, releases_error = safe(lambda: effects.fetch_json(f"https://api.github.com/repos/{repository}/releases?per_page=30"))
    newest_release = newest_stable_release(releases, prefix)
    newest_any_tag = newest_tag(tags.keys(), prefix)

    observed_commit = tags.get(approved_tag)
    dist = component["distribution"]  # type: ignore[index]
    package_sources = (
        [npm_latest(str(dist["package"]), effects)] if dist["mechanism"] == "npm"  # type: ignore[index]
        else [nuget_versions(str(dist["package"]), effects)] if dist["mechanism"] == "nuget"  # type: ignore[index]
        else [])

    def verify_artifact(artifact: Mapping[str, object]) -> Mapping[str, object]:
        url = asset_url(component, str(artifact["name"]))
        if not download or url is None:
            return {"name": artifact["name"], "expectedSha256": artifact["sha256"], "url": url, "status": "NOT_RUN"}
        data, error = safe(lambda: effects.fetch_bytes(url))
        actual = sha256(data) if isinstance(data, bytes) else None
        return {
            "name": artifact["name"], "url": url, "expectedSha256": artifact["sha256"], "actualSha256": actual,
            "bytes": len(data) if isinstance(data, bytes) else None,
            "status": "PASS" if actual == artifact["sha256"] else "FAIL", "error": error,
        }

    artifacts = [verify_artifact(a) for a in component["artifacts"]]  # type: ignore[union-attr]
    tag_status = ("PASS" if observed_commit == component["commit"]
                  else "FAIL" if observed_commit else "UNRESOLVED")
    newest_available = (str(newest_release["tag_name"]) if newest_release else newest_any_tag)
    return {
        "systemId": component["systemId"],
        "repository": repository,
        "role": component["role"],
        "required": component["required"],
        "distributionClass": component["distributionClass"],
        "distribution": dist,
        "approvalSource": "kemiller2002/echelon-registry channels/echelon-current/linux-x64.json",
        "newestApprovedStable": {"version": component["version"], "tag": approved_tag,
                                 "releaseStage": component["releaseStage"], "lifecycleState": component["lifecycleState"]},
        "newestAvailable": {
            "githubRelease": str(newest_release["tag_name"]) if newest_release else None,
            "githubReleasePublishedAt": newest_release.get("published_at") if newest_release else None,
            "newestTag": newest_any_tag,
            "githubReleasesError": releases_error,
            "packageSources": package_sources,
            "tag": newest_available,
        },
        "approvedIsNewest": (semver_key(newest_available or "") or (0, 0, 0)) <= (semver_key(approved_tag) or (0, 0, 0)),
        "registryCommit": component["commit"],
        "observedTagCommit": observed_commit,
        "tagCommitCheck": tag_status,
        "tagsError": tags_error,
        "releaseManifestSha256": component["releaseManifest"]["sha256"],  # type: ignore[index]
        "artifacts": artifacts,
        "artifactStatus": ("NOT_RUN" if all(a["status"] == "NOT_RUN" for a in artifacts)
                           else "PASS" if all(a["status"] == "PASS" for a in artifacts) else "FAIL"),
    }


def inventory(channel_set: Mapping[str, object], registry_commit: str, effects: Effects, download: bool,
              now: str) -> Mapping[str, object]:
    components = [discover_component(c, effects, download) for c in channel_set["components"]]  # type: ignore[union-attr]
    statuses = reduce(lambda acc, c: acc | {c["tagCommitCheck"], c["artifactStatus"]}, components, set())
    return {
        "schema": "causa-r002.release-inventory/1",
        "discoveredAt": now,
        "approvalAuthority": {
            "repository": "kemiller2002/echelon-registry",
            "commit": registry_commit,
            "channel": "echelon-current",
            "profile": channel_set["profile"],
            "catalogSnapshot": channel_set["catalogSnapshot"],
            "platform": channel_set["platform"],
        },
        "discoveryStatus": "DISCOVERED_FAIL" if "FAIL" in statuses else "DISCOVERED_NOT_VERIFIED",
        "note": "Discovery records identities and digests. Compatibility is established only by the smoke tests in compatibility-matrix.json.",
        "components": components,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--registry", type=Path, required=True, help="echelon-registry checkout")
    parser.add_argument("--platform", default="linux-x64")
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--download", action="store_true", help="download and digest-check every artifact")
    args = parser.parse_args()
    channel_set = json.loads((args.registry / "channels/echelon-current" / f"{args.platform}.json").read_text("utf-8"))
    registry_commit = subprocess.run(["git", "-C", str(args.registry), "rev-parse", "HEAD"],
                                     check=True, capture_output=True, text=True).stdout.strip()
    result = inventory(channel_set, registry_commit, Effects(fetch_json, fetch_bytes, ls_remote), args.download,
                       datetime.now(timezone.utc).isoformat(timespec="seconds"))
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(result["discoveryStatus"])
    return 1 if result["discoveryStatus"] == "DISCOVERED_FAIL" else 0


if __name__ == "__main__":
    raise SystemExit(main())
