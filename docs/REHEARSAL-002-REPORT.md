# Causa Rehearsal 002 — final report

Run `causa-rehearsal-002`, started 2026-10-23 12:14:13Z, accepted 2026-10-24 00:15:24Z. Machine-readable record:
`evidence/acceptance/run.json` and the generated ledger `evidence/acceptance/ledger.json`
(`python3 tools/ledger/ledger.py validate`). Every number below is taken from those files, CI runs or live-run
artifacts; nothing is estimated.

## Identity

| | |
|---|---|
| Repository | `kemiller2002/indy-init-rehearsal-002` |
| Accepted source | `main` @ `45644a7689c28f9f46e6110a2553b8c006b5060b` |
| Deployed URL | https://kevinmmiller.us/indy-init-rehearsal-002/ (GitHub Pages; `build-info.json` names the served commit) |
| Deployed commit | `45644a7…` — served `build-info.json` names it; deployed acceptance passed (main run 40043306191, 00:15:24Z) |
| Storage contract | `causa-storage-v1.0.0` (frozen fixtures `sha256:f650a2e5…2c2b86`) |
| Data repository (live tests) | `kemiller2002/causa-data`, branch `claude/causa-rehearsal-002-kickoff-i2ihli`, `workspaces/demo/…` only |
| Rehearsal 001 | read only; never modified |

## Stack (frozen; `evidence/stack/stack-lock.json`)

Approved set `echelon-current` 1.25.0. Praxis 3.16.0 · Ordo 1.17.0 · Percepta 0.1.0 · Dokimos 0.6.0 · Aegis.Core
1.0.0 · Limen 0.9.0 (npm, and F# Contract/Guest/Routing via Conditor's verified NuGet feed) · Forma 0.9.0 · Folio
0.4.0 · Conditor 0.10.8 · .NET SDK 10.0.402 · Node 24.22.1 · Playwright 1.64.1 · axe-core/playwright 4.14.0 ·
TypeScript 7.0.2. Newer but unapproved (not selected, DEP-CURRENT-002): Praxis 3.19.0, Dokimos 0.7.0, Folio 0.4.1.
Arca, Fides, Strata: not applicable. Stack defects DEF-STACK-001…008 are in `evidence/stack/compatibility-matrix.json`.

## Acceptance ledger

| Gate | Status | Evidence |
|---|---|---|
| G0 Scope | PASS | pinned requirements Indy-init@b5d9d79, trace of 72 requirement ids |
| G1 Dependencies | PASS | stack verified as a set (C01–C16), lock checked on every CI run |
| G2 Interfaces | PASS | view contract; engine and browser tests on both sides |
| G3 Domain | PASS | vendored F# domain; illegal transitions and forged histories refused |
| G4 Frozen data | PASS | 5 golden scenarios, 90 negative fixtures, contract identities |
| G5 UI | PASS | 61 browser tests (loading, keyboard, axe, responsive, color, all P0 states) |
| G6 Persistence | PASS | real GitHub provider, causa-data run 40041756242: L1–L6 PASS on 2f8fb98 (application sources identical to the candidate) |
| G7 Defect class | PASS | six classes closed; every code correction in the candidate independently reviewed (closure reviews 1–11) |
| G8 Candidate | PASS | full CI on the candidate |
| G9 Deployment | PASS | served commit = candidate; deployed acceptance run 40043306191 (00:15:24Z, 527 s) |
| G10 Measurement | partial | token usage is not exposed to the session; CI time recorded for the accepted run only |

Requirements: 72 traced; 66 PASS, 6 NOT_RUN, 0 BLOCKED. NOT_RUN: UI-002 causal graph, UI-007 search, UI-008
comparison, UI-009 export, UI-012 first-run guidance (all P1, deferred, UD-2), ADRP-016 competition compliance (not
assessed).

**Accepted: yes** (the predicate: G0–G9 PASS on the candidate SHA, no open critical class, served commit = candidate).

## Defect classes (`docs/decisions/DC-00N.md`)

| Class | Severity | Invariant | Review rounds that found variants | State |
|---|---|---|---|---|
| DC-001 | critical | a publication is parented by the state it was decided on | 1 | closed |
| DC-002 | high | an answer applies only to the question that asked it; events only in legal states | 3 | closed (reviews 7, 8) |
| DC-003 | medium | after a save is sent, no failure is reported as "not written" | 7 (security; closure 1, 2, 7–10) | closed (review 11); residual F6 low |
| DC-004 | high | an advisory provider field never gates or grants | 2 (live) | closed |
| DC-005 | high | reads of mutable state are never cached; a save applies only to the head it was decided on | 8 (3 live; closure 2–6) | closed (reviews 7, 8) |
| DC-006 | medium | a failed framework download ends the start promptly, without breaking the loader's retry | 3 | closed (review 5) |

The decisive change came late: review 6 proved that GitHub's REST API cannot compare-and-swap a branch (a stale read
or a race could re-publish over a reset head). On the owner's decision the save became one GraphQL
`createCommitOnBranch` guarded by `expectedHeadOid`, which GitHub applies atomically or refuses. Reviews 7–11 then
found only DC-003 variants: an applied save whose answer was lost could still be described as "not written". Those
were closed with a "may have applied" flag and recognition of a save by its command id (CONTRACT §5.4).

Known residuals (documented, low): DC-003 F6 — if someone removes our applied save before we confirm it, confirming
re-adds only our change; DC-006 — a fast persistent download failure shows the wrong failure message; info-level
wording notes I1/I2 in reviews 9 and 11.

What found what: independent engine tests found DC-001; the security review found DC-002/003; the **real GitHub
provider** found DC-004 (twice) and DC-005 (three times); deployed acceptance found DC-006; eleven independent closure
reviews found the remaining variants, each fixed with a test that fails on the earlier code.

## Metrics (from `run.json`)

| Milestone | Time (UTC) | Elapsed |
|---|---|---|
| Start | 10-23 12:14:13 | 0 |
| First successful build | 12:26:32 | 12.3 min |
| First functional screen | 12:58:34 | 44.4 min |
| First external-data load (5/5 verified) | 12:59:36 | 45.4 min |
| First green CI | 13:58:28 | 104.3 min |
| First verified remote write (run 39975799085, commit 33dcdde) | 15:10:31–15:11:41 | 176.3–177.5 min |
| First candidate on main (c4d5bbf) | 15:17:27 | 183.2 min |
| First deployed, identity-verified build with deployed acceptance (5608698) | 17:11:00 | 296.8 min |
| Accepted candidate deployed and verified (45644a7, main run 40043306191, 527 s) | 10-24 00:15:24 | 721.2 min |

Candidates on main: 7. Defect classes that recurred after a claimed fix: 5 (DC-002, 003, 004, 005, 006). Independent
reviews: 1 security review + 11 closure reviews. Live real-GitHub runs: 17 (first full PASS: run 8; the last six all
PASS). Token usage: unavailable to the session.

## Comparison with Rehearsal 001

Baseline from `docs/requirements/DEFECT-CLASS-CLOSURE-AND-RELEASE-GATES.md`: first green CI T+88.3 min, accepted
release T+261.8 min, four adversarial review rounds, recurring forged-history failures.

- First green CI: R002 T+104.3 min vs R001 T+88.3 min (+16.0 min). R002 spent its first hour on Phase 0 stack
  discovery and verification, which the mission made blocking.
- Accepted release: R002 T+721.2 min vs R001 T+261.8 min (+459.4 min). A deployed, identity-verified build existed at
  T+296.8 min; the remaining seven hours went to closing defect classes to the independent-review standard (DCC-005),
  including replacing the REST write path after review 6.
- Review rounds: R002 12 (security + 11 closure) vs R001 4. Forged histories did not recur in R002 (the vendored
  domain and 90 negative fixtures held throughout); the recurring classes were provider semantics (caching, advisory
  fields, concurrency, compare-and-swap) and honesty of outcome reporting.
- Scope differs: R002 has zero bundled data and a real external GitHub store with optimistic concurrency; R001 shipped
  frozen demonstration data. Per the requirements, no causal improvement is claimed from one uncontrolled run.

## Unresolved

- Branch protection (`.github/branch-protection.json`) is prepared but needs repository admin to apply (UD-1).
- P1 features deferred (UD-2). Praxis DEF-STACK-007 needs an upstream fix (UD-3).
- UD-4 (a merge before a review reported) is resolved: reviews 4–11 are recorded and their findings fixed before the
  accepted candidate.

## Recommendations for the next rehearsal

1. Run the live provider suite from the first write onwards: every high-severity class found after first green CI
   was a provider behaviour the mocks did not model.
2. Check the provider's concurrency primitives before designing the write path. The REST write sequence took five
   review rounds to prove unsafe; GitHub's atomic, guarded GraphQL commit was available from the start.
3. Keep "may have applied" as explicit state from the first design: most late findings were outcome-reporting
   variants of DC-003.
