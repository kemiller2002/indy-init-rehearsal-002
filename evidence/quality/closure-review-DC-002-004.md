# Independent closure review — DC-002, DC-003, DC-004

- **Reviewer:** independent agent (no edits to `src/` or `tests/`; nothing committed or pushed).
- **Reviewed:** `git diff c731c34 c92eadf -- src/engine tests/App.Engine.Tests` (fix commits 3cd37f3, c92eadf), with
  `src/engine/Shell.fs`, `Session.fs`, `GitHub.fs`, `View.fs` read at c92eadf (working tree).
- **Date:** 2026-10-23.

## What was run

1. `dotnet test tests/App.Engine.Tests -c Release` → **40 passed, 0 failed** (unmodified repo suite).
2. Five probe tests (P1–P5) in a throwaway project in the session scratchpad, **outside the repository tree**. The
   project referenced `src/engine/App.Engine.fsproj` and copies of the repo's `Fixtures.fs`, `FakeGitHub.fs` and
   `Harness.fs` without changes. Run with `CAUSA_DATA_DIR=.cache/causa-data dotnet test -c Release`. Result:
   P2–P5 ran to completion and printed the behaviour described below. P1 **threw `KeyNotFoundException`** from inside
   `Shell.handle`. The scratch project was deleted afterwards. The sketches below are the exact sequences it ran.
   One helper is not in the repo: `pumpHolding pred` answers pending effects in FIFO order but sets aside those
   matching `pred`, so they can be delivered late.

## Summary

| Class | Verdict | Worst remaining variant |
| --- | --- | --- |
| DC-002 (answers/events applied to the wrong question/state) | **OPEN** | P2, high: the re-apply never resolves when the reloaded directory is invalid at listing time. The held change drops out of view and all writes stay blocked. |
| DC-003 (post-PATCH / outcome-unknown classification) | **OPEN** | P1, high: reconciling a pre-PATCH `Unknown` after the branch moved makes the engine throw (`Session.treeRequest` with missing blob shas) |
| DC-004 (absent provider field read as denial) | **CLOSED** (low residuals) | P4, low: a rate-limit 403/429 is held as `WriteDenied`, so Retry is not offered |

---

## DC-002 — OPEN

### What the fix does close (evidence)

- Every request is recorded as `(Op, generation)` (`Shell.fs:53-62`). A write answer is applied only when
  `InFlight(w, _)` has `w.Generation = generation` and the generation is not zero (`Shell.fs:489-492`). Refresh answers
  (generation 0) are applied only while `Refreshing` (`Shell.fs:488`).
- `startWrite` refuses while `InFlight` and assigns a fresh generation from the counter (`Shell.fs:121-127`).
  `sync.retry` and `auth.renew` go through it (`Shell.fs:551, 581`).
- Events are checked against state:
  - `connect` only from Disconnected/Refused (`Shell.fs:84-87`).
  - discard only from `Held` (`Shell.fs:562`), reapply only from `Held Conflict` (`Shell.fs:571`), reconcile only from
    `Held Unknown` (`Shell.fs:556`), retry only from `Held NotSent|WriteFailed` and not refreshing (`Shell.fs:550`).
  - `writeAllowed` (`Session.fs:159-169`).
  - A new id must not collide with the catalog (`Shell.fs:145-148`).
- Blob reads are content-addressed (`OpBlob(id,file,sha)`). They apply only to an entry that is `Fetching` and still
  lists that sha (`Shell.fs:318-322`).
- If the reload fails, a pending re-apply returns to a hold (`Shell.fs:284-290`). The same happens when the target is
  missing or new (`Shell.fs:308-312`), or when its blobs come back invalid or unreadable (`Shell.fs:330-331`).
- Tests 15, 15b and 15c pass.

### Remaining variants

**P2 — high — a re-apply whose reloaded directory is invalid at listing never resolves, and the held change
disappears from view.**

`Session.catalogOf` marks a malformed or oversized directory `Invalid` at once and issues **no** blob reads for it
(`Session.fs:189-203`). `onRefresh` keeps `Reapply = Some w` for any target that still exists (`Shell.fs:313`). The
only code that resolves a re-apply is in `onBlob` (`Shell.fs:327-331`), and it never runs because there are no blob
reads. The result:

- `Sync = Idle` with `Reapply = Some w`. `syncState` reads "clean" and `hasHeld` / `canDiscard` are false: the change
  is no longer visible and cannot be discarded.
- `writeAllowed` refuses every write with "A held change is being re-applied…" (`Session.fs:167`).
- A refresh repeats the same outcome.
- Only a disconnect clears it, and the disconnect silently drops the change.

This contradicts DC-002's claim that "if … the target becomes unreadable it returns to a visible hold". Observed
output:

```
P2 Sync=Idle Reapply.IsSome=true Refreshing=false status="Invalid …"
P2 syncState=clean hasHeld=false canDiscard=false canCreate=false createReason=A held change is being re-applied to the latest version.
P2 after refresh: Reapply.IsSome=true hasHeld=false
P2 after discard: Reapply.IsSome=true canCreate=false
```

Failing-test sketch:

```fsharp
let held = connected run repo |> withRule isPatch (Concurrent(otherWriterAppends prefix target "other")) |> observe target "mine"
// Held Conflict
let broken = { held with Repo = held.Repo |> commitFiles "stray" [ prefix + target + "/stray.txt", Some "x"B ] }
let after = broken |> fire "sync.reapply"
// expected: Held(_, Conflict _), hasHeld = true, Reapply = None
// actual:   Sync = Idle, Reapply = Some, hasHeld = false, canCreate = false (permanently)
```

The same gap also covers a target that falls outside the 40-entry display limit. That case is handled, but the hold
reports "no longer in this workspace" (`Shell.fs:308`), which is misleading.

**P3 — medium — a late blob answer from an earlier listing starts the re-apply against the pre-reload catalog.**

The re-apply trigger in `onBlob` (`Shell.fs:328`) checks only that the entry is `Valid` and `Sync = Idle`. It does not
check that the entry was produced by the reload that `sync.reapply` asked for. Any blob answer for the same
investigation id that arrives after `sync.reapply` and before the reload's tree answer passes that check, including
one whose `wants` matched nothing. It then re-decides against the **old** catalog entry and `live.Head`, which is the
state that caused the conflict.

DC-001's base-commit parenting stops an overwrite: the write is refused as non-fast-forward, then held as Conflict
again. The cost is a wasted second publication and a spurious second conflict. It is still an answer applied to a
state it was not asked for. Observed:

```
P3 reapply started from stale state: BaseCommit=old head? true Refreshing=true
P3 final: Held Conflict
P3 PATCHes total=2
```

Sequence:

1. Connect.
2. `refresh`, holding back its blob reads.
3. A second `refresh`, answered fully, so the catalog is `Valid`.
4. Observe the target, with another writer appending to it at the PATCH. Result: Held Conflict.
5. `deliver sync.reapply`, but before its `GET ref` is answered, deliver one held-back blob read for the target.
6. **Expected:** nothing starts until the reload lists the target. **Actual:** `InFlight` with
   `BaseCommit = old head`.

Two refresh chains can run at once because `refresh` does not refuse while `Refreshing` (`Shell.fs:110-114`). Only the
UI flag `canRefresh` hides it. `sync.discard` during a refresh, and `sync.reapply` while `Refreshing`, also start a
second chain.

**P5 — low — `sync.reconcile` reuses the held write's generation.**

`sync.reconcile` calls `emitGen w.Generation` (`Shell.fs:557`). Answers to sibling requests of the same generation that
were still outstanding when the write was held are therefore accepted during the reconciliation. For example, the
other two `OpVerifyBlob` reads. One late error aborts a valid reconciliation back to `Unknown` (observed:
`P5 … Held Unknown`). A late `Ok` only adds content-identical bytes, so it does no harm. Fix: give each reconcile a
fresh generation, as `startWrite` does.

Sketch:

1. Submit, holding back the three verification `GET blob` reads.
2. Answer the first with `NetworkDown`. Result: Held Unknown.
3. `deliver sync.reconcile`.
4. Answer the second held-back read with `NetworkDown`.
5. **Expected:** stays `InFlight Reconciling`. **Actual:** `Held Unknown`.

**Low — event gating is inconsistent with `Refreshing`.** `sync.retry` refuses while refreshing; `auth.renew`
(`Shell.fs:579`) and `sync.reconcile` (`Shell.fs:556`) do not. I traced the cases where a refresh answer lands
mid-write. Head and Catalog are replaced (`Shell.fs:306`), and `consistent` (`Shell.fs:460`) then either moves Head
correctly or forces another refresh. I found no wrong result, so I rate this hygiene only.

**Test note:** test 15 pokes `"sync.refresh"`, which is not an event name; the event is `"refresh"`. It therefore
never exercises refresh during a write. The engine does refuse that case (`Shell.fs:112`), but the test does not prove
it.

---

## DC-003 — OPEN

### What the fix does close (evidence)

- `afterRefSent` turns every failure at `UpdatingRef | Reconciling | Verifying*` into `Unknown` (`Shell.fs:339-346`).
- A read-back that does not match is `Unknown` (`Shell.fs:432, 473`).
- `Cancelled` maps to `OutcomeUnknown` (`Shell.fs:77`). `OutcomeUnknown` from the PATCH auto-reconciles
  (`Shell.fs:367`).
- `canRetry` is shown only for `NotSent | WriteFailed` (`View.fs:140`).
- Discarding an `Unknown` hold says the change "may already be on GitHub" (`Shell.fs:565`).
- `updateRef` is only ever called with a sha that passed the 40-hex check (`GitHub.fs:197, 230`) or with
  `w.Commit = Some c` (`Shell.fs:362, 374`). I found no path that sends a PATCH with an empty or unknown sha.
- Tests 11c and 16 pass.

### Remaining variants

**P1 — high — reconciling a pre-PATCH `Unknown` after the branch moved crashes the engine.**

A cancelled or outcome-unknown `POST /git/blobs` is held as `Unknown` (`holdFor`, `Shell.fs:245`) with `Blobs`
incomplete and `Commit = None`. On `sync.reconcile`, if the branch moved for any reason, the engine:

1. goes `OpReconcileRef` with sha ≠ BaseCommit and `Commit = None` (`Shell.fs:381`);
2. goes `ReloadingCommit` → `ReloadingTree`, finds the investigation unchanged, and rebases (`Shell.fs:404-408`);
3. calls `Session.treeRequest`, which indexes `w.Blobs[f]` (`Session.fs:238`) and throws
   `KeyNotFoundException` inside `Shell.handle`.

Only the unmoved-branch case (`Shell.fs:376`, "publish again from the blobs") was covered. In the browser host the
response is never produced, so the write stays `InFlight(Reconciling)` with no open request. While it is in flight
there is no Retry, Discard or Refresh, so the change can only be lost by disconnecting.

Failing-test sketch (observed exception and stack: `Session.fs:238` ← `Shell.fs:408` ← `Shell.fs:491`):

```fsharp
let held = connected run repo |> withRule isPostBlob (UnknownAfter false) |> createInvestigation "P1" "cancelled blob"
// Held(w, Unknown _) with w.Blobs.Count = 0, w.Commit = None
let moved = { held with Repo = held.Repo |> commitFiles "unrelated" [ "README.md", Some "changed\n"B ] }
let after = moved |> fire "sync.reconcile"
// expected: verified publication (or a hold); actual: KeyNotFoundException
```

Fix direction: when the rebase finds `Blobs` incomplete, restart at `CreatingBlobs` instead of `CreatingTree`. More
generally, route every "no commit was created" reconciliation through `CreatingBlobs`.

**Low — an `Unknown` hold cannot renew its credential.** A 401 after the PATCH is correctly `Unknown`. But `auth.renew`
only accepts `WriteDenied` (`Shell.fs:579`), so with an expired token every reconcile fails again with `Unknown`. The
only way out is to discard, and the refresh that follows the discard also fails. The user can confirm the outcome only
by disconnecting and reconnecting with a new token, and the held change is lost.

**Low — misleading wording after reconciliation.** If reconciliation proves by ancestry that the commit was not
applied, a network failure in the `Reloading*` stages that follow is classed as pre-PATCH (`Shell.fs:342`). It then
reads "Nothing was sent." and offers Retry. Retry is safe here because compare showed the commit is not in the branch
history. The wording is wrong, since a PATCH was sent.

One edge remains: the compare status `behind` means the branch was reset to an ancestor of BaseCommit. A late
application of the original non-forced PATCH would then still be a fast-forward. This is theoretical.

---

## DC-004 — CLOSED (low residuals)

### Evidence

- `parseRepository` reads `permissions.push` as `bool option`. If `permissions` or `push` is absent it gives `None`
  (`GitHub.fs:216-222`).
- Only `Some false` refuses writing (`Session.fs:161`). That is the only place in the engine that gates writes on
  `CanPush`, confirmed by grep across `src/engine`.
- The status names the case: "write not reported" and "GitHub decides" (`View.fs:60-64, 235-238`).
- A write refused at write time is held as `WriteDenied` and can be renewed (`Shell.fs:244`, `View.fs:141`).
- Test 17b passes: the repository answer omits `permissions`, and the investigation is created and verified.

### Credential handling on `auth.renew`

The token is removed from `ConnectForm` whether the renewal is accepted or refused (`Shell.fs:576-582`). It is held only
in `Live.Token` and sent only in the `Authorization` header (`Shell.fs:25-43`). `ConnectForm` projects only
owner/repo/branch/root (`View.fs:92-104`). Test 18 asserts that the token key is gone. No leak found.

One note: a token typed with `connect.token` while connected, but never followed by `auth.renew`, stays in
`ConnectForm` (memory only, never projected) until disconnect.

### Low residuals of the "provider field as claim" lesson

- **P4 (observed)** — a rate-limit 403 (`"API rate limit exceeded…"`) or a 429 before the PATCH is held as
  `WriteDenied` (`holdFor` maps `Forbidden _` to it, `Shell.fs:244`). The result is `canRetry=false`,
  `canRenew=true`, with the detail "…Wait and retry." No Retry is offered: the user must paste a token, even the same
  one. This is a transient condition handled as a denial. Expected: `Forbidden true` → `NotSent` or `WriteFailed`
  (retryable).
- Rate-limit detection depends on body text only (`GitHub.fs:204-206`), because no response headers are requested
  (`Shell.fs:41`). A rate-limit 403 whose body lacks "rate limit" is read as access denied.
- An absent tree-entry `size` is treated as within the 4 MiB cap (`Session.fs:184`), and `parseBlob` enforces no size.
  Absence therefore gives permission here. This is low, because GitHub always reports `size` for blobs in recursive
  trees.

---

## Recommended closure actions

1. **DC-002/P2:** in `onRefresh`, when `Reapply = Some w` and the re-listed target's status is already `Invalid`,
   return it to `Held(w, Conflict …)`. Add P2 as a regression test.
2. **DC-002/P3:** run the re-apply only from an entry produced by the reload that `sync.reapply` started. For example,
   check `Reapply` together with `not Refreshing` in `onBlob`, or record the head the re-apply waits for. Also make
   `refresh` refuse while `Refreshing`.
3. **DC-002/P5:** give `sync.reconcile` a fresh generation.
4. **DC-003/P1:** restart at `CreatingBlobs` when the rebase path has incomplete `Blobs`. Add P1 as a regression test.
   This is the release blocker among these findings, because it throws inside `Shell.handle`.
5. **DC-004/P4:** map `Forbidden true` to a retryable hold.
6. **Tests:** fix test 15 to send `"refresh"` instead of `"sync.refresh"`.
