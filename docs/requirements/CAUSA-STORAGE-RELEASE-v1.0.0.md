# Causa storage contract v1.0.0: release identity

**Status: RELEASED.** The tag `causa-storage-v1.0.0` (annotated tag object `fd2991a392e913b741f0ed2bc4daa4bc4be57f6c`)
points to commit `c947c7e354be80766573a782546c8186336774cc` of `kemiller2002/causa-data`. It was created by the
repository's `release-tag` workflow (run 39874016465) only after that exact commit passed every gate anchored to the
tag, the 17-case self-test and all three consumers.

Experiment record: `kemiller2002/research-documents`, `research/experiments/EX-CAUSA-STORAGE-2026-001/` (RESULT.md).

## Identities

| identity | value |
|---|---|
| contract version | `1.0.0` |
| release tag | `causa-storage-v1.0.0` (tag object `fd2991a3…`) → `c947c7e354be80766573a782546c8186336774cc` |
| schema identity: SHA-256 of `contract/v1.0.0/schemas/bundle.json` | `0a68af8c93190d2e4d6c27beb2f8cb67c3aebdda586d01beb7122b71af179152` |
| dataset identity: SHA-256 of `fixtures/v1.0.0/manifest.json` | `f650a2e509a420872b46177b21f57ef6ebd7625ae4c69dd92198d3ca472c2b86` |
| release identity: SHA-256 of `compatibility/v1.0.0/lock.json` | `6a7e7802ff2a3ff500ab6065662514a96e09e6c15eb30212ba1377fcc5bf8fc5` |
| domain engine | `indy-init-rehearsal-001@55f49f61c939a3a780cee592f36f732a1328f981` |
| engine assembly: SHA-256 of `IndyInitRootCauseInvestigator.Engine.dll`, sterile build | `b3904277e72390667a35ccaeb7986f5d6c4848ffd5b8c28fc13f9673b5e13a44` |
| .NET SDK | `10.0.402` (`rollForward: disable`) |

## Contents

- **Registry:** 31 event types, 107 payload fields, 60 JSON Schemas (Draft 2020-12), 284 schema fields.
- **Fixtures:** 5 frozen scenarios A–E, 184 events, plus 90 negative fixtures.

## Authority

- The normative contract is `causa-data/contract/v1.0.0/CONTRACT.md` at the tag.
- [CAUSA-DATA-CONTRACT-v1.md](CAUSA-DATA-CONTRACT-v1.md) was the *candidate* that this release was reconciled against.
  Where the two differ, CONTRACT.md wins. The differences are listed in the experiment's `COMPATIBILITY-DECISIONS.md`.

## How R002 and the competition consume it

1. Pin the tag `causa-storage-v1.0.0` and check that the three identities above match. Any mismatch is a FAIL
   (STORAGE-007).
2. Run `tools/fetch-engine.sh`, then `causa gate . --anchor causa-storage-v1.0.0` in causa-data.
3. Write only under `workspaces/demo/<runId>/`, never under `fixtures/`. The provider token stays in the local store.

## How the tag was created

The agent session that froze the release could not push tags. `.github/workflows/release-tag.yml` in causa-data does
it instead. It:
- reads `.github/release-tag/request.txt`;
- checks that the commit is in the branch history and that `RELEASE` names the tag;
- tags the commit locally and runs every gate anchored to that tag;
- only then pushes the tag.

It never moves or replaces an existing tag. Protecting `causa-storage-v*` with a tag ruleset is recommended.
