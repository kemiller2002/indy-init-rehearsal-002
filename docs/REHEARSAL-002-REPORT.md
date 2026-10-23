# Causa Rehearsal 002 — final report

Run `causa-rehearsal-002`, 2026-10-23, started 12:14:13Z. Machine-readable record: `evidence/acceptance/run.json` and the
generated ledger `evidence/acceptance/ledger.json` (`python3 tools/ledger/ledger.py validate`). Every number below is
taken from those files, CI runs or live-run artifacts; nothing is estimated.

## Identity

| | |
|---|---|
| Repository | `kemiller2002/indy-init-rehearsal-002` |
| Accepted source | `main` @ `5608698c63f5f7f447edb716b3fcd5536b5282d2` (tree `1ce2a0e`) |
| Deployed URL | https://kevinmmiller.us/indy-init-rehearsal-002/ (GitHub Pages; `build-info.json` names the served commit) |
| Deployed commit | DEPLOYED_PLACEHOLDER |
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
| G5 UI | PASS | 60 browser tests (loading, keyboard, axe, responsive, color, all P0 states) |
| G6 Persistence | PASS | real GitHub provider, causa-data run 39990408889: L1–L6 PASS on the accepted tree |
| G7 Defect class | **BLOCKED** | fixes for the third review's findings merged on the owner's instruction before the fourth independent review reported (UD-4) |
| G8 Candidate | PASS | full CI on the candidate |
| G9 Deployment | G9_PLACEHOLDER |
| G10 Measurement | partial | token usage is not exposed to the session; CI time recorded for the accepted run only |

Requirements: LEDGER_SUMMARY_PLACEHOLDER. NOT_RUN: UI-002 causal graph, UI-007 search, UI-008 comparison, UI-009
export, UI-012 first-run guidance (all P1, deferred), ADRP-016 competition compliance (not assessed).

**Accepted: no.** The predicate requires G0–G9 all PASS; G7 is BLOCKED. Nothing in this report converts that.

## Defect classes (`docs/decisions/DC-00N.md`)

| Class | Severity | Invariant | Rounds | State |
|---|---|---|---|---|
| DC-001 | critical | a publication is parented by the state it was decided on | 1 | closed (independent tests + review) |
| DC-002 | high | an answer applies only to the question that asked it; events only in legal states | 3 | fixes merged; last low residual not re-reviewed |
| DC-003 | medium | after the ref update is sent, no failure is reported as "not written" | 2 | closed by review 2 |
| DC-004 | high | an advisory provider field never gates or grants | 2 (live) | closed by review 2 and live runs |
| DC-005 | high | reads of mutable state are never answered from a cache; reloads never move back | 4 (3 live) | live PASS; last fix not re-reviewed |
| DC-006 | medium | a failed framework download ends the start promptly, without breaking the loader's retry | 2 | last fix not re-reviewed |

Known residual (documented in DC-005): a force-reset of the branch to exactly our parent, immediately after our PATCH,
is indistinguishable from a lagging replica by any single read.

What found what: independent engine tests found DC-001; the security review found DC-002/003; the **real GitHub
provider** found DC-004 (twice) and DC-005 (three times) — none of these were reachable with the mocks; deployed
acceptance found DC-006; three independent closure reviews found 12 further variants, each fixed with a test that fails
on the earlier code.

## Metrics (from `run.json`)

| Milestone | Time (UTC) | Elapsed |
|---|---|---|
| Start | 12:14:13 | 0 |
| First successful build | 12:26:32 | 12.3 min |
| First functional screen | 12:58:34 | 44.4 min |
| First external-data load (5/5 verified) | 12:59:36 | 45.4 min |
| First green CI | 13:58:28 | 104.3 min |
| First verified remote write (run 39975799085, commit 33dcdde) | 15:10:31–15:11:41 | 176.3–177.5 min |
| First candidate on main (c4d5bbf) | 15:17:27 | 183.2 min |
| Accepted candidate deployed and accepted on the served site | ACCEPTED_PLACEHOLDER |

Candidates on main: 5. Defect classes that recurred after a claimed fix: 4 (DC-002, 003, 004, 005). Independent
reviews: 1 security review + 3 closure reviews (a fourth pending). Live real-GitHub runs: 10 (first full PASS: run 8).
Token usage: unavailable to the session.

## Comparison with Rehearsal 001

Baseline from `docs/requirements/DEFECT-CLASS-CLOSURE-AND-RELEASE-GATES.md`: first green CI T+88.3 min, accepted
release T+261.8 min, four adversarial review rounds, recurring forged-history failures.

- First green CI: R002 T+104.3 min vs R001 T+88.3 min (+16.0 min). R002 spent its first hour on Phase 0 stack
  discovery and verification, which the mission made blocking.
- Release: R002 COMPARISON_PLACEHOLDER
- Review rounds: R002 4 (security + 3 closure), comparable to R001's 4. Forged histories did not recur in R002 (the
  vendored domain and the 90 negative fixtures held throughout); the recurring classes were provider-semantics
  classes (caching, advisory fields, concurrency of answers) that only the live provider exposed.
- Scope differs: R002 has zero bundled data and a real external GitHub store with optimistic concurrency; R001 shipped
  frozen demonstration data. Per the requirements, no causal improvement is claimed from one uncontrolled run.

## Unresolved

- G7 BLOCKED: record the fourth independent closure review of 63b2232 (UD-4), then regenerate the ledger.
- Branch protection (`.github/branch-protection.json`) is prepared but needs repository admin to apply (UD-1).
- P1 features deferred (UD-2). Praxis DEF-STACK-007 needs an upstream fix (UD-3).

## Recommended next action

Record the fourth closure review; if it closes DC-002/005/006, set G7 to PASS with
`python3 tools/ledger/run_record.py <sha> <deployed> <accepted_at> PASS PASS <seconds>` and
`python3 tools/ledger/ledger.py generate`. For the next rehearsal, run the live provider suite from the first write
onwards: every high-severity class this run found after the first green CI was a provider behaviour the mocks did not
model.
