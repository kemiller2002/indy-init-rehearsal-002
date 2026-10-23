# Closure review 6: DC-005 class fix (794f886)

Scope: `git diff 1670dcb 794f886 -- src tests`, read against `evidence/quality/closure-review-5.md` (R1–R4) and the
last section of `docs/decisions/DC-005.md`. Nothing under `src/` or `tests/` was modified.

## Verdict: OPEN (narrowed a lot; the absolute claim is false)

The commit is a real improvement:

- R1 and R2 are closed.
- R4's write consequence is closed whenever the base-check read is fresh.
- No PATCH can overwrite another writer or force.

The DC-005 text claims that "with the check, no sequence of stale reads can re-publish removed commits". That claim
does not hold:

- **One** stale ref answer at the base check re-publishes commits that a reset removed (V1).
- A real reset that lands between the base check and the PATCH does the same (V2).

Both are probe-reproduced. The engine's own threat model assumes lagging ref answers, which is why the re-read
exists. Under that model the base check is only as strong as the single read it makes.

## What was run

1. `source …/scratchpad/env.sh && cd /home/user/indy-init-rehearsal-002 && dotnet test tests/App.Engine.Tests -c Release`
   at HEAD `794f886`, with a clean tree. Result: **Passed 60, Failed 0, Total 60**.
2. Probe project: a copy of the test project at
   `/tmp/claude-0/-home-user/665f3fcc-18ff-5e63-937e-80d2d01e3679/scratchpad/probe7`.
   - Its ProjectReference points at the repo's `src/engine` (absolute path), and `CAUSA_DATA_DIR` was set.
   - Probes P1–P6 were appended to the copied `EngineTests.fs`.
   - It ran with `dotnet test -c Release --filter …`.
   - The directory was deleted afterwards.
   - An incidental failure of the copied `19c` is unrelated: it uses a repo-relative file path that does not exist in
     the copy.

## (1) Safety: can a PATCH re-publish removed commits, overwrite, or force?

**Force: no.** `GitHub.updateRef` always sends `"force": false` (`src/engine/GitHub.fs:172-176`). No other PATCH
builder exists.

**Overwrite another writer: no.** Every PATCH sends a commit whose parent is `w.BaseCommit`. A non-forced update
succeeds only if the current head is an ancestor of that commit. So any head carrying another writer's commit that
is not in our base refuses with 422 and goes to the reload path (`Shell.fs:415-417`). This holds independently of
every read.

**Re-publishing removed commits: yes, in two ways.** The danger case is a head that is a *proper ancestor* of
`w.BaseCommit`, that is, a reset. Then the non-forced PATCH fast-forwards and restores `BaseCommit` and everything
between. The only guard is the single fresh `getRef` in `OpCheckBase` (`Shell.fs:404-413`).

### Entry points to a PATCH

There are exactly two PATCH sites:

| Site | Location | Reached from |
| --- | --- | --- |
| `OpCheckBase` | `Shell.fs:407` | `startWrite`: submit, `sync.retry`, `auth.renew` on WriteDenied, `reapply`. Also the rebase in `OpReloadTree` (`:463-470`, via tree → commit → check) and `OpReconcileRef` with `Commit = None` (`:427`). |
| `OpReconcileRef` when `sha = w.BaseCommit` with `Commit = Some c` | `Shell.fs:423-425` | `OpWriteRef → OutcomeUnknown`, and the `sync.reconcile` / `auth.renew`-on-Unknown handlers (`:622`, `:654`). |

The second site bypasses `OpCheckBase`. Its own fresh ref read applies the same test (`sha = BaseCommit`), so it is
equivalent in strength: also one read, not weaker.

### V1 (medium): one stale ref answer at the base check re-publishes a removed commit

**Probe P1:**

```fsharp
let w0 = connected run (workspaceRepo run); let h0 = w0.Repo.Head
let saved = w0 |> createInvestigation "First" "later removed by a reset"; let c1 = saved.Repo.Head
let reset = { saved with Repo = { saved.Repo with Head = h0 } }            // someone resets; no reload in this tab
let second = reset |> withRule isGetRef (Respond(200L, refBody c1)) |> createInvestigation "Second" "after reset"
```

**Observed:**

- `sync=Idle` and `parents=["68ca40f"]` (= c1).
- `isAncestor repo c1 head = true`. The assertion "c1 re-published" fails.

The save was verified and reported as successful. This is the ordinary case: the tab simply has not reloaded since
the reset, and one lagging answer is enough. No reload sequence is needed.

**Fix direction:** a single read cannot close this. Options:

- Compare-and-swap on the server: GraphQL `updateRefs` with `beforeOid`, or `createCommitOnBranch` with
  `expectedHeadOid`.
- Short of that, require the base-check answer to agree with an independent confirmation, for example a `compare`
  of BaseCommit against the ref, or a second fresh read. This only lowers the probability.

The DC-005 sentence should be corrected either way.

### V2 (low–medium): a reset between the base check and the PATCH (a real race)

**Probe P2:**

```fsharp
saved |> withRule isPatch (Concurrent(fun r -> { r with Head = h0 })) |> createInvestigation "Second" "race"
```

Here `Concurrent` applies the reset immediately before the PATCH is handled.

**Observed:** `sync=Idle`, `parents=["c1444d0"]` (= c1), and c1 is back on the branch. This is a TOCTOU that a
REST non-forced PATCH cannot close. The window is one round trip, and the overwrite requires a concurrent
*rewind* specifically. Only server-side CAS (see V1) closes it. It should be documented as a residual, not claimed
closed.

**Reset to exactly `w.BaseCommit`:** this is not a removal. It is publishing on the current head, which is correct.

## (2) Does the base check break anything?

**Stuck writes / loops: no.** Every exit of `OpCheckBase` leads to one of: PATCH, Held Conflict at
`Attempts >= maxRebases` (`Session.fs:146`, `= 3`), the reload path, or `fail`. `Attempts` is incremented on every
rebase (`Shell.fs:463`).

**Probe P5:** the branch moves (a README commit) on every ref read. Observed:
`Held(…, Conflict "The branch kept moving while saving.") patches=0`, and the PATCH was never forced.

**Stage classification: correct, with one wording quirk.** The check runs at stage `CreatingCommit`
(`Shell.fs:404`), so `afterRefSent = false` and failures go to `holdFor`. That is right: no PATCH was sent.

- `Offline` with `Commit = Some _` gives WriteFailed "not on the branch yet" (`:249`). Correct.
- Quirk (low): an `OutcomeUnknown` (timeout) on the base-check **GET** becomes `Unknown` (`:254`). That marks the
  outcome as possibly applied, although a GET before any PATCH cannot have written anything.
  - `canRetry` is false for Unknown. The user must reconcile, and reconcile reaches a correct result through
    `OpReconcileRef`.
  - This is misleading, not unsafe. Found by code reading, not probed.

**Moved / consistent after a check-triggered rebase: correct.** The rebase sets `Moved = true`, so after the verified
save `consistent = false` and `refresh` runs (`Shell.fs:524,535`).

**Probe P3:** reset to h0, no reload, fresh base check. Observed:

- `sync=Idle` and the new commit's parent is h0, so c1 was not re-published.
- After the automatic reload, `view head = known = new commit`.

**New false conflict (low; availability, not safety).** A stale *older* answer at the base check sends a change to the
reload path on a commit the branch has moved past. If this investigation differs there, a spurious Conflict is held,
although the PATCH would have fast-forwarded.

**Probe P6:** `observe target "first"`, then `observe target "second"` with one `Respond(200, refBody <parent of
c1>)` on getRef. Observed:

- Held `Conflict "Someone else changed this investigation on GitHub after you loaded it…"`.
- The repo head equals c1, so in fact nobody else changed anything.

It is visible and recoverable (reapply), and nothing is written. Before 794f886 this save would have succeeded.

## (3) Known bookkeeping

`Known` is set at connect (`Shell.fs:286`), on every adopted reload (`OpTree`, `:339`), and on every verified save
(`:531`). No other place adopts a head, so the coverage is complete.

**Can it move to a commit not on the branch? Yes**, through two adopt paths that accept an answer without confirming
it:

- **`OpRefreshReread`, `Ok again -> adopt again` (`Shell.fs:328`).** A second answer that differs from the first is
  adopted with no compare.
  - **Probe P4:** reset to h0, then `fire "refresh"` with the 2nd ref read answered stale c1. Observed: `view=6727897
    known=6727897 repo=de74cd0`, that is, view = Known = c1 while GitHub holds h0.
  - The removed commit is shown with no reset notice. Known now names a commit off the branch.
- **`OpRefreshCompare`, 404/422 adopt (`:304`).** This adopts the ref read without confirmation. Lower concern.

Known is used only by the reload's compare (`:308`). `OpCheckBase` uses `w.BaseCommit`, so an off-branch Known
affects the view, not publication safety. P4 continued: the next save with a fresh base check rebased onto h0, and
c1 was not re-published. With a stale base check it is V1.

## (4) Remaining display-only variants

- **D1 (low):** the variant DC-005 already concedes. Two consecutive stale *older* answers adopt an older commit as a
  "reset" until the next reload. Nothing is written on it.
- **D2 (low):** P4 above. After a real reset, a stale *newer* re-read re-shows the removed commit as current, with no
  notice. Known moves off the branch until the next reload. Nothing is written on it unless V1 also occurs.
- **D3 (low):** the P6 false conflict.

## Out of diff, noted by code reading only (not probed)

`OpWriteRef → OutcomeUnknown → OpReconcileRef → (sha = BaseCommit) → PATCH` (`Shell.fs:418-425`) has no `Attempts`
bound. A provider that keeps timing out on the PATCH loops automatically. This predates 794f886.

## Summary

| ID | Severity | Status |
| --- | --- | --- |
| R1 | – | closed (test 25; anchor = Known) |
| R2 | – | closed (`Rejected(422L, _)` only) |
| R3 | low | display-only, acknowledged (D1) |
| R4 | – | write consequence closed with a fresh check (P4); display part remains (D2) |
| V1 | medium | one stale base-check read re-publishes removed commits (P1) |
| V2 | low–medium | reset race between check and PATCH re-publishes removed commits (P2) |
| D2 | low | stale newer re-read shows removed commit; Known off branch (P4) |
| D3 | low | stale older base-check answer gives a spurious Conflict (P6) |
| Q | low | base-check GET timeout classified Unknown |
