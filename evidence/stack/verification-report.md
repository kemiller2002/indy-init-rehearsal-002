# Causa Rehearsal 002 — Echelon stack verification report

Run: `causa-rehearsal-002`. Discovery: 2026-10-23 (see `release-inventory.json` `discoveredAt`). Lock frozen 2026-10-23T13:15Z.
Machine-readable evidence: `release-inventory.json`, `compatibility-matrix.json`, `stack-lock.json`, `logs/`.

## How "newest approved stable" was decided

The approval authority is the Echelon Registry, not GitHub's "latest release": `kemiller2002/echelon-registry`
channel `echelon-current` (profile 1.25.0, resolved set `channels/echelon-current/linux-x64.json`,
sha256 `06026da529d5408a7363793790a0eed32b3f5414246fe4509a216adb27d403e3`). Its components are `releaseStage: stable`,
`lifecycleState: active`. Newest *available* releases were discovered separately at each actual release source
(GitHub releases, `git ls-remote` tags, npm, nuget.org).

The Indy-init discovery inputs were starting points only. They point at `echelon-foundry/*` repositories that do not
exist and equate "latest GitHub release" with approval (DEF-STACK-004). `tools/stack/discover_stack.py` replaces them.

## Selected stack (exact; see stack-lock.json for SHAs and digests)

| Component | Newest available | Newest approved stable | Selected | Applicability |
|---|---|---|---|---|
| Praxis | v3.19.0 | v3.16.0 | 3.16.0 | applicable |
| Ordo | v1.17.0 | v1.17.0 | 1.17.0 | applicable |
| Percepta (repo lifecycle) | v0.1.0 | v0.1.0 | 0.1.0 | applicable |
| Dokimos | v0.7.0 | v0.6.0 | 0.6.0 | applicable |
| Aegis (EchelonFoundry.Aegis.Core) | 1.0.0 | 1.0.0 | 1.0.0 | applicable |
| Limen (npm) | 0.9.0 | 0.9.0 | 0.9.0 | applicable |
| Limen F# (NuGet release assets) | 0.9.0 | 0.9.0 | 0.9.0 | applicable |
| Forma | 0.9.0 | 0.9.0 | 0.9.0 | applicable |
| Folio | v0.4.1 | v0.4.0 | 0.4.0 | applicable |
| Conditor | v0.10.8 | v0.10.8 | 0.10.8 | applicable |
| Arca | 0.5.0 | 0.5.0 | — | not applicable (contract layout; see lock note) |
| Fides | 0.2.0 | 0.2.0 | — | not applicable (needs a hosted exchange) |
| Strata | 0.1.1 | 0.1.1 | — | not applicable (PostgreSQL) |
| Visual/Communication Engineering, Tutela, Summa contracts | — | in channel | — | not applicable to the application build |
| .NET SDK | 10.0.402 | 10.0.402 | 10.0.402 | applicable |
| Node.js | 24.22.1 (24 LTS) | — | 24.22.1 | applicable (Folio requires Node 24) |
| Playwright / axe-core / TypeScript | 1.64.1 / 4.14.0 / 7.0.2 | — | same | applicable |
| Causa storage contract | causa-storage-v1.0.0 | released | 1.0.0 | applicable (three identities verified) |
| Causa domain engine | — | rehearsal-001@55f49f61 | vendored byte-identical | applicable (semantic oracle) |

Praxis 3.19.0, Dokimos 0.7.0 and Folio 0.4.1 are published but not in the Registry channel; they were not selected
(DEP-CURRENT-002: newer than approved needs explicit approval). Nothing was downgraded to an R001 version: R001 used
Forma 0.5.0/0.9.0, Folio 0.3.0, Ordo 1.5.0; this run uses Forma 0.9.0, Folio 0.4.0, Ordo 1.17.0.

## Verification performed

Every artifact listed by the channel was downloaded and its SHA-256 compared with the Registry release manifest
(16/16 PASS); every tag was resolved with `git ls-remote` and compared with the Registry's recorded commit (PASS; Aegis
is tagged by NuGet identity and its commit 56935c88 exists in kemiller2002/aegis). Then, as one system:

- C01–C05: Conditor qualified the selection, installed host tools with digest checks, bootstrapped the empty
  repository (`conditor init`), installed the web packages (byte-identical to the approved artifacts), and opted in
  the Limen F# packages through its NuGet release-asset feed.
- C06–C08: the vendored domain and contract, Aegis 1.0.0 and Limen.Contract/Guest 0.9.0 build on SDK 10.0.402 with
  warnings as errors; the causa-data gate anchored to `causa-storage-v1.0.0` passes; R002's own build replays the
  5 golden scenarios and rejects all 90 negative fixtures with the expected codes (94/94).
- C09–C10, C12–C16: the Limen kernel and the .NET WebAssembly engine complete the protocol 1.4 handshake in Chromium;
  Forma's BrandCompiler accepts the Causa brand with its contrast gate; TypeScript 7 type-checks the kernel against
  Limen's declarations; Praxis intake and a Dokimos snapshot run.
- C11, C14 (Folio print rendering, axe-core scans): see `compatibility-matrix.json` for their current result.

## Defects found during stack verification

DEF-STACK-001 (Folio pinned from npm by the scaffold; fixed), DEF-STACK-002 (FSharp.Core 10.1.402 has two byte-distinct
artifacts; mitigated), DEF-STACK-003 (Conditor overrides the environment's git auth with GH_TOKEN; worked around),
DEF-STACK-004 (Indy-init discovery sources; replaced), DEF-STACK-005 (Conditor indy-init preset lags echelon-current;
explicit manifest), DEF-STACK-006 (installation registration needs a target id; low). Details and reproductions are in
`compatibility-matrix.json`.

## Lock and drift

`stack-lock.json` is FROZEN. `tools/stack/check_lock.py` fails CI if package-lock, NuGet references, the vendored
feed, conditor.json, global.json or the vendored sources differ from it. Upstream releases after the freeze are
recorded as drift (`drift.json`), not introduced.

## Verdict

G1 (dependencies): the selected stack installs cleanly and its cross-component smoke tests pass, with the defects
above recorded. Nothing in this report claims a compatibility result that was not executed.
