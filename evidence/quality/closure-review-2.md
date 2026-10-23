# Independent closure review 2: DC-002, DC-003, DC-004, DC-005

- **Reviewer:** an independent agent. It changed nothing under `src/` or `tests/` and committed or pushed nothing.
  This file is the only file it wrote in the repository.
- **Date:** 2026-10-23. **Working tree:** HEAD `01b3a9f`. `git diff 3eb7995 HEAD -- src tests` is empty, so the reviewed
  code is the code at `3eb7995`.
- **Scope correction:** the requested range `9b3613c..3eb7995` does **not** contain `5388739` (P1–P5) or `c4d5bbf`
  (DC-004), because both are older than `9b3613c`. I therefore reviewed `git diff c92eadf 3eb7995 -- src/engine
  tests/App.Engine.Tests`. That range covers 5388739, c4d5bbf, 20f74ff and ef028c0, and starts at the commit the
  earlier review covered. I also read `src/engine/Shell.fs`, `Session.fs`, `GitHub.fs` and `View.fs` in full, plus
  `src/wasm/Program.cs`.
- **Unrelated working-tree change:** `tests/browser/boot.spec.js` shows as modified in `git status`. It was already
  modified when this review started, and I did not touch it.

## What was run

1. `source …/scratchpad/env.sh && cd /home/user/indy-init-rehearsal-002 && dotnet test tests/App.Engine.Tests -c Release`
   → **Passed 48, Failed 0**.
2. Six probe tests (A–F) in a throwaway xunit project at
   `/tmp/claude-0/-home-user/665f3fcc-18ff-5e63-937e-80d2d01e3679/scratchpad/probe2`, outside the repository tree:
   - It referenced `src/engine/App.Engine.fsproj` and unmodified copies of `Fixtures.fs` (only the data path made
     absolute), `FakeGitHub.fs` and `Harness.fs`.
   - It was run with `dotnet test -c Release --logger "console;verbosity=detailed"`. All six ran to completion.
   - Their output is quoted below. Each probe prints what it observes and asserts nothing, so "Passed" means only that
     it ran.
   - The project was deleted afterwards.

## Summary

| Class | Verdict | Worst remaining variant |
| --- | --- | --- |
| DC-002 (answers/events applied to the wrong question or state) | **OPEN** | A, **medium**: a refresh started before an `auth.renew` write can land after that write is verified. It rolls the head and catalog back to the pre-write listing, and the just-saved investigation vanishes from view. |
| DC-003 (post-PATCH / outcome-unknown classification) | **CLOSED** (low residuals) | D, low: after a rebase, reconciling an unknown commit creation re-sends a PATCH of the *pre-rebase* commit. GitHub refuses it as non-fast-forward, so the only cost is one rebase attempt. |
| DC-004 (reported role must never gate writing) | **CLOSED** | B, low (UX): renewal is offered but refused while a reload runs, and the typed token is silently discarded. |
| DC-005 (mutable-state reads never answered from a cache) | **CLOSED** (low note) | C, low: the post-PATCH `OpVerifyRef` result is ignored. "Verified" proves the commit's content, not that the commit is on the branch. |

---

## DC-002 — OPEN

### Earlier variants

- **P2 — closed.**
  - `onRefresh` now returns a re-apply whose reloaded target is `Invalid`/`Unreadable` at listing to
    `Held(w, Conflict …)` (`Shell.fs:320-325`).
  - Test `20b` checks `Held Conflict`, `Reapply = None`, `hasHeld` and `canDiscard`. It passes.
  - The other ways out are unchanged: reload failure (`Shell.fs:291-297`), a missing target (`:315-316`), a new
    investigation (`:317-319`), and invalid or unreadable blobs (`:344-345`).
  - I found no remaining state with `Reapply = Some` and no pending read that could end it. Probe E also covers
    `sync.reapply` pressed while a refresh is already running: no new effects are emitted, the running refresh lists
    the target, and the re-apply completes:
    `E after reapply: Idle Reapply=true newEffects=0` → `E final: Idle Reapply=false patches=2`.
- **P3 — closed.**
  - `onBlob` starts or ends a re-apply only when `not next.Refreshing` (`Shell.fs:342-344`).
  - `refresh` refuses while `Refreshing` (`Shell.fs:115`), so only one refresh chain exists at a time.
  - A late blob from an older listing can only fill a `Fetching` entry that lists the same sha. That entry is
    content-identical, so it is harmless.
  - Test `20c` passes.
- **P5 — closed.** `sync.reconcile` gives the write a fresh generation, `app.Counter + 1` (`Shell.fs:577`), and so
  does `auth.renew` on an `Unknown` hold (`:605`). The counter is never reset, so a generation equals the first
  correlation number of its own batch and is unique. Test `20e` passes.
- **Test note — closed.** Test 15 now sends `"refresh"` (`EngineTests.fs:745`).

### New variant A — medium: a stale refresh overwrites a verified write

**What goes wrong.** `auth.renew` on a `WriteDenied` hold (`Shell.fs:600-602`) does not check `live.Refreshing`.
`sync.retry` (`:569`), `sync.reconcile` (`:575`) and `auth.renew` on an `Unknown` hold (`:603`) all do. A write can
therefore start while a refresh chain is outstanding:

- The write verifies with `consistent = true` (`Shell.fs:479-491`). It sets `Head` to the written commit but keeps
  `Refreshing = true` from `live`.
- The refresh's remaining generation-0 answers are then still accepted, because `Shell.fs:507` checks only
  `generation = 0 && live.Refreshing`.
- `onRefresh` replaces `Head` and `Catalog` with the listing read **before** the write (`Shell.fs:313`).

**Reachable from the UI:**

- While a change is `Held`, `canRefresh` is true (`View.fs:145`).
- While refreshing, `canRenew` is true (`View.fs:141`).

**Consequence:**

- The investigation that was just saved and announced as verified disappears from the catalog. On its route the user
  sees "No investigation with this identifier exists".
- `headLabel` shows the pre-write head.
- No remote data is lost. The next decision is parented on the stale head, so DC-001 forces non-fast-forward, then a
  reload or a conflict. A manual refresh corrects the view.

This is an answer applied to a state it was not asked for. It is the DC-005 symptom of "stale view after my own
write", produced inside the engine.

**Repro.** This is probe A, using the harness from `tests/App.Engine.Tests/Harness.fs`:

1. `connected "pa" (workspaceRepo "pa") |> withRule isPostBlob (Respond(401L, "{\"message\":\"Bad credentials\"}"))
   |> createInvestigation "Renew" "during refresh"` → `Held WriteDenied`, with `canRefresh=true` and `canRenew=true`.
2. `deliver (event "refresh" …)`. Answer only the queued `GET ref`, which returns head H0. Set the resulting
   `GET commit H0` aside.
3. Clear the rules, then `send "connect.token" token |> fire "auth.renew"`. The write completes:
   `A renewed: Idle LastVerified=true live.Head=786c4b0 repoHead=786c4b0 catalog=6 notice=Saved to GitHub and verified …`.
4. Deliver the set-aside `GET commit`, then `pump`:
   `A after late refresh: live.Head=2973e46 (H0=2973e46, written=786c4b0) catalog=5 Refreshing=false sync=Idle`, and
   `headLabel=2973e46`.

Expected: `live.Head = 786c4b0` with 6 investigations, or a refresh that re-reads after the write. Actual: the view
rolls back to H0 with 5 investigations.

A side observation from an earlier run of the same probe: when the renewed token differs from the old one, the
outstanding refresh request still carries the **old** token. It fails with 401, and a "Reload failed" notice follows
the "Saved … verified" notice. This is benign but confusing.

**Fix direction:**

- Add `&& not live.Refreshing` to the `WriteDenied` branch of `auth.renew`.
- More robustly, after a verified write, drop or restart any refresh that is still running. For example, tag refresh
  answers with a refresh generation, or call `refresh` again when `Refreshing` was true.
- Add probe A as a regression test.

---

## DC-003 — CLOSED (low residuals)

### Evidence

- **P1 — closed.** When the rebase path finds incomplete `Blobs`, it restarts at `CreatingBlobs` with `Blobs = Map.empty`
  (`Shell.fs:422-427`). It no longer calls `Session.treeRequest` with missing keys. Test `20` (pre-PATCH unknown, then
  branch moved, then reconcile) ends `Idle` and verified.
- **P4 — closed.** `Forbidden true` (rate limit) maps to `WriteFailed`, which is retryable (`Shell.fs:250`). Test `20d`
  passes.
- **Post-PATCH failures are never offered as Retry.**
  - Any failure at `UpdatingRef | Reconciling | Verifying*` is `Unknown` (`Shell.fs:353-359`).
  - `canRetry` is shown only for `NotSent | WriteFailed` (`View.fs:140`).
  - `Reloading*` stages are reached only after a definitive non-fast-forward (`:378-380`), or after reconciliation
    showed the commit is not in the branch history (`:395, :399`). Retry is safe there.
- **"Nothing was sent" is no longer claimed once a commit exists.** `Offline` with `w.Commit.IsSome` gives
  `WriteFailed("The change is not on the branch yet…")` (`Shell.fs:247`).
- **An `Unknown` hold can now renew its credential.** `auth.renew` on an `Unknown` hold reconciles under the new token
  with a fresh generation instead of re-publishing (`Shell.fs:603-607`), and `canRenew` includes `Unknown`
  (`View.fs:141`).
- **No PATCH with an unknown sha.** `updateRef` is called only with `Some commit` from `parseCreatedSha`, which enforces
  40 hex characters (`GitHub.fs:207, 239`). The calls are at `Shell.fs:376` and `:388`.

### Residual D — low: a stale pre-rebase commit is re-PATCHed during reconciliation

The rebase (`Shell.fs:421`) keeps `Commit = Some <pre-rebase commit>`. Suppose the commit creation after the rebase is
cancelled or has an unknown outcome. The hold is `Unknown`, and reconciliation reads `ref = BaseCommit`. It then
re-sends `PATCH` with the pre-rebase commit (`Shell.fs:386-388`). That commit is parented on the old base, so GitHub
refuses it as non-fast-forward. The engine reloads, rebases again and succeeds.

The PATCH is non-forced and its sha is a commit that really exists, so it is safe. The costs:

- one wasted round trip;
- one `Attempts` consumed, out of `maxRebases = 3`. Near that limit this becomes a spurious "The branch kept moving"
  conflict hold. That hold is still visible and loses nothing.

Repro (probe D):

1. `connected "pd" … |> withRule isPatch (Concurrent(otherWriterAppends prefix openIds[1] "other"))
   |> withRule <second POST /git/commits> (UnknownAfter false) |> createInvestigation "D" …`
   → `Held Unknown`, `Commit=1e72f59`, `patches=1e72f59`.
2. Clear the rules, then `fire "sync.reconcile"` → `Idle`, `patches=1e72f59,1e72f59,f2ccb22`.

Fix: set `Commit = None` when rebasing (`Shell.fs:421`).

### Other residuals (low, carried over)

- The compare status `behind` (branch reset to an ancestor) still lets a late original non-forced PATCH be a
  fast-forward. This is theoretical.
- See probe B under DC-004 for the renewal-while-refreshing UX gap.

---

## DC-004 — CLOSED

### Evidence

- The `CanPush = Some false` gate was removed from `Session.writeAllowed` (`Session.fs:159-168`). `writeAllowed` now
  gates only on `Target.writable` (frozen fixtures versus workspace), `Sync`, `Reapply` and `Refreshing`.
- `grep -n CanPush src/engine/*.fs` finds only the type (`GitHub.fs:27`), the parser (`GitHub.fs:230`, where an absent
  `permissions` gives `None`) and view labels (`View.fs:60-64, 235-239`). Every gate (`View.fs:206-229, 445`, and
  `Shell.submit:198`) goes through `writeAllowed`. The role gates nothing.
- A real refusal is held as `WriteDenied` with the change kept and renewable (`Shell.fs:251`). A rate-limit 403 is
  retryable (`:250`).
- Tests `17b` (role absent, save succeeds), `17c` (`push:false`, save succeeds), `17d` (GitHub refuses, change held,
  `canRenew`) and `18` (renewal) pass.

### Credentials and `auth.renew`

- In every branch, the token is removed from `ConnectForm` before matching (`Shell.fs:597-598`).
- The token lives only in `Live.Token` and the `Authorization` header (`Shell.fs:36`). It is not projected.
- Test `18` asserts that the form key is gone. Test `12` covers the response redaction.
- No leak found.

### Residual B — low (UX)

On an `Unknown` hold while a refresh runs, the engine refuses `auth.renew` (`Shell.fs:603`). Yet `canRenew=true` is
projected (`View.fs:141`), and the typed token is discarded with the misleading notice "Enter a new token to retry the
held change."

Probe B output:

- `B Held Unknown Refreshing=true canRenew=true canReconcile=true`
- `B after renew: Held Unknown tokenInForm=false liveTokenChanged=false notice=Enter a new token to retry the held change. effects=0`

`canReconcile` has the same mismatch with `sync.reconcile` (`:575`).

Fix: project `canRenew` and `canReconcile` as false while `Refreshing`, or word the notice as "wait for the reload".

---

## DC-005 — CLOSED (low note)

### Every read of mutable state is unique per request

- `GitHub.getRef` and `GitHub.getRepository` are the only constructors with `Fresh = true` (`GitHub.fs:111-112, 120-121`).
- Every ref read in the engine goes through `GitHub.getRef`:
  - connect (`Shell.fs:266`) and refresh (`:116`);
  - verify (`:377`) and reload (`:380`);
  - reconcile (`:381`, `:578`, `:607`).
- The repository read happens only at connect (`:105`).
- No other request names a branch except the PATCH (`GitHub.fs:172-176`), which is never cached.
- `compare` takes two commit SHAs (`GitHub.fs:125`), so it is content-addressed.
- The nonce is `Ids.derive Context.Seed (1_000_000 + Counter)` (`Shell.fs:63`). Within one message the counter
  increases. Across messages and page loads, the seed is `Guid.NewGuid()` per `Dispatch` call
  (`src/wasm/Program.cs:21`), and nothing else calls `withContext`/`dispatch` in `src/`. Because SHA-256 is applied
  over (seed, n), URLs are unique with overwhelming probability across sessions.
- The offset of 1,000,000 keeps the nonce indices apart from the `fresh n` command identifiers (small `n`,
  `CommandForms.fs:403-405`). Because of the hash, the nonce reveals nothing about those identifiers.
- Probe F ran a non-fast-forward rebase, an unknown PATCH that applied, an unknown blob creation followed by
  reconciliation, and a refresh. Output: `F Idle ref/repo reads=9 allFresh=true distinct=true` and
  `F addressed=80 anyFresh=false kinds=GET repo;GET ref;GET commit;GET tree;GET blob`.
- Test `21` checks uniqueness across two engines with different seed ranges.

### Content-addressed reads do not need the nonce

Commit, tree (`?recursive=1` by tree sha) and blob reads are immutable by sha, so caching them is correct. Their URLs
carry no `fresh=` parameter (probe F, test 21).

### `OpVerifyRef` proceeds when the ref differs from the written commit (`Shell.fs:430-435`)

The two `Ok` branches (`:432` and `:433`) are identical, so the result of the ref read is ignored. Given the PATCH, I
judge this **acceptable but over-claimed**:

- **Acceptable.** A 200 on a non-forced PATCH is GitHub's authoritative statement that the branch fast-forwarded to
  the commit. A ref read that disagrees immediately afterwards is most likely a lagging replica or cache: the exact
  DC-005 phenomenon, which the live run showed GitHub's shared cache produces. Treating that as failure would turn
  good saves into false `Unknown` holds. The commit, tree and blob read-back then proves that GitHub stores exactly
  the bytes written.
- **Gap (low).** The read-back proves content, not that the commit is reachable from the branch. Probe C, with a
  force-reset of the branch right after the PATCH, gave `C forced-reset: Idle LastVerified=true live.Head=dd7db71
  repoHead=c1f1627 notice=Saved to GitHub and verified by an independent read-back (commit dd7db71).` A stale ref
  answer is likewise accepted (`C stale-read: Idle LastVerified=true`).

  When the ref moved past the commit because another writer appended, `consistent` stays true and `Head` is set to
  our commit, not the branch tip. That is harmless: the next write goes non-fast-forward and reloads.

Suggested hardening: when `sha <> commit`, run `compare commit...sha`, as reconciliation already does.

- `identical` or `ahead`: verified, with `Moved = true` so that a refresh follows.
- `behind` or `diverged`: re-read once with a new nonce. If it still disagrees, hold the write as `Unknown`.

At minimum, remove the duplicate branch so the code does not suggest a check it does not make.

---

## Recommended actions

1. **DC-002 / A (medium):** add `not live.Refreshing` to `auth.renew` on a `WriteDenied` hold (`Shell.fs:600`). In
   addition, discard or restart an outstanding refresh once a write verifies. Add probe A as a regression test.
2. **DC-003 / D (low):** set `Commit = None` on rebase (`Shell.fs:421`).
3. **DC-004 / B (low):** gate `canRenew` and `canReconcile` on `not Refreshing`, or explain the refusal instead of
   discarding the token silently.
4. **DC-005 / C (low):** use the compare check described above when the verify-ref answer differs, or remove the dead
   duplicate branch at `Shell.fs:432-433`.
5. **Scope:** future closure reviews should use a range that contains the fix commits. `9b3613c..3eb7995` omits
   5388739 and c4d5bbf.
