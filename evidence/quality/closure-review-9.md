# Closure review 9: review 8 N1 correction

- **Reviewed SHA:** `8d53d70`, compared with `43690a0`.
- **Scope:** `git diff 43690a0 8d53d70 -- src tests`. That is `src/engine/Shell.fs` (the OpReloadTree fall-through,
  OpRecognise not-found, and `sync.discard`) and test `27` in `tests/App.Engine.Tests/EngineTests.fs`.
- **Method:**
  - Read every consumer of `MayHaveApplied` at `8d53d70`.
  - Ran the full suite in a scratch worktree of `8d53d70`, with `.cache/causa-data` linked in.
  - Added four scratch probes (R9P1 to R9P4). They were never committed. The worktree was removed afterwards.
  - Ran test 27 and the probes against `43690a0`'s `Shell.fs`.
- **Probe source:** kept at `/tmp/claude-0/-home-user/665f3fcc-18ff-5e63-937e-80d2d01e3679/scratchpad/r9-probes-EngineTests.fs`.
  The probes are at the end of that file.

## Verdicts

| Item | Verdict |
| --- | --- |
| N1 (landed then removed save reported "not written"; discard "nothing was written") | **CLOSED** as scoped. This covers both the OpReloadTree path and the Recognising not-found path. One adjacent low residual remains: **N2** below. |
| DC-003 for this delta | **Unaffected**. The class is strengthened and nothing regresses. |
| DC-002 for this delta | **Unaffected** |
| DC-005 for this delta | **Unaffected** |

**Suite at `8d53d70`:**
- Causa.Conformance.Tests: 94/94 pass.
- App.Engine.Tests: 69/69 pass. With the scratch probes added, 72/72 pass. R9P4 is an observation probe and asserts nothing.

## (1) N1 closure

### OpReloadTree path (investigation gone, or no `events.ndjson`)

- The new `elif w.MayHaveApplied` branch (`Shell.fs:445-448`) catches this case before the "Your change was not
  written" fall-through.
- That fall-through is now reachable only while the flag is false. The flag is false only when no send was ever
  answered with unknown, so the claim is true there.

### Recognising not-found path (`Shell.fs:461-463`)

- The write now keeps the flag, and the wording is the same "not on the branch now (it may have been saved and then
  removed)".
- **R9P1** (save applied with the answer lost, then another writer reverts the investigation to its base bytes and
  appends its own event):
  - At `8d53d70`: Conflict with "may have been saved", flag `true`. Discard says "It is not on the branch now". Zero
    copies of our observation are on the branch.
  - At `43690a0`: the probe fails with flag `false`. So discard there said "nothing was written". This confirms that
    part of N1 was real and is now fixed.

### Discard (`Shell.fs:617`)

- The `_ when w.MayHaveApplied` arm comes after `Unknown` and before the fallback.
- Every non-Unknown hold that carries the flag now gets "It is not on the branch now". That includes the onRefresh
  re-apply failure holds.
- "Nothing was written" is now reachable only with the flag false.

## (2) Consumers of `MayHaveApplied` on a Conflict hold carrying `true`

| Consumer | Behaviour for a flagged Conflict |
| --- | --- |
| `fail` (`:388`), NotFastForward cap (`:402`), OpReloadTree recognition (`:441`, `:445`) | Only run while the write is InFlight. A Conflict hold is not in flight, so none of them sees it. |
| `sync.retry` (`:594`) | Accepts only `NotSent`/`WriteFailed`, so this is a no-op. R9P3 checks that the state and request count are unchanged. |
| `sync.reconcile` (`:602`) | Accepts only `Unknown`, so this is a no-op (R9P3). |
| `auth.renew` (`:626`) | Accepts only `WriteDenied`/`Unknown`. From a Conflict, even with a token typed, it shows the notice "Enter a new token to retry the held change." No request is sent and the hold is unchanged (R9P3). This is pre-existing behaviour and harmless. The notice wording is slightly misleading for a Conflict, but that is cosmetic. |
| `sync.reapply` (`:621`), then `reapply` (`:218-235`) | On a successful re-decision, `reapply` resets the flag to false and `Attempts` to 0, then starts a fresh write. **R9P2** runs reapply from the Recognising not-found conflict: Idle, verified, exactly one copy of our observation, exactly one extra save. If the reload cannot re-apply, the onRefresh/onBlob holds keep `w` with the flag. **R9P3** runs reapply after a deletion: "no longer in this workspace; your change was not re-applied", flag still `true`, and discard says "not on the branch now". |
| Later "genuinely unsent" failure shown as Unknown? | **Not reachable from this Conflict.** The only way back into flight is `reapply`, and it resets the flag. The flag is per-Write, and `disconnect` drops the write. So keeping the flag cannot turn a later NotSent or WriteFailed into Unknown. If it could, that would be the conservative direction anyway, which is acceptable under DC-003. |
| View (`View.fs:129`, `:145`, `:674`) | Does not read the flag. A Conflict shows its detail plus "Your change is preserved here", and offers `canReapply`. Nothing is new here. |

Resetting the flag in `reapply` is correct for the new attempt:
- The earlier guarded send can no longer land, because the branch has moved off its base. The only exception is the
  F6 residual.
- Any "not written" text the new attempt produces describes the new attempt.

## (3) Decision classes

- **DC-003:** The delta only removes false "not written" and "nothing was written" claims, and keeps a flag that no
  in-flight consumer can see from a hold.
  - The previously guarded paths still pass: tests 16, 22d, 26–26f.
  - The F6 residual is unchanged. If a new investigation landed and was then deleted entirely, `now = BaseEntries`
    (empty), so it is re-published (this is the F6 republish case, documented and pre-existing).
- **DC-002:** Discard is still accepted only from `Held`. The code only adds a binding of `w` to choose the text.
  Generations, the reapply and retry guards, and `startWrite`'s refusal during a reload are untouched. Tests 15, 15b,
  15c, 20b, 20c, 20e and 22 pass.
- **DC-005:** No read, URL, nonce or ancestry logic changed. Tests 21, 22b, 23, 23b, 24b and 24c pass.

## (4) Test 27

- Test 27 does exercise N1 through the OpReloadTree path. The save is applied with the answer lost (`UnknownAfter
  true`), and the `getRef` hook deletes every file of the investigation. That lands in the new `:445` branch.
- It asserts the Conflict wording and the discard wording, and that exactly one save was sent.
- It fails on `43690a0` with "Sub-string found", as claimed in DC-003.md.
- **Gap (info):** test 27 does not cover the Recognising not-found change at `:463`, which is the part where the flag
  is now kept. R9P1 covers it and fails on `43690a0`. Consider committing it as `27b`.

## Findings

### N2: low. A re-apply after an N1 conflict on a new investigation says "your new investigation was not written"

- **Reproduction (R9P4):**
  1. `createInvestigation` with save `UnknownAfter true`.
  2. On the `getRef` that follows, another writer deletes only `events.ndjson` from the newly created directory.
  3. The hold is Conflict "…not on the branch now (it may have been saved and then removed)", with flag `true` and
     `IsNew` true. This part is correct.
  4. Fire `sync.reapply`. The onRefresh `Some w when w.IsNew` arm (`Shell.fs:342-344`) holds Conflict "An investigation
     with this identifier now exists; your new investigation was not written.", with the flag still `true`.
- **Why it is wrong:** our save landed, and the directory still holds our other files. So "was not written" is false.
  It is the same false claim as N1, reached one step later.
- **Discard afterwards:** correctly says "It is not on the branch now".
- **Not a regression:** the delta did not introduce this. The message predates it, and at `43690a0` the first hold
  already said "not written".
- **Why low:** another writer must alter our freshly created, randomly identified directory before we reconcile.
- **Suggested fix:** make the onRefresh conflict texts consult `w.MayHaveApplied` the same way, for example "…was not
  re-applied; it may have been saved and then removed". Add R9P4 as a regression test.

### I1: info. Cosmetic notice

From a Conflict hold, `auth.renew` shows "Enter a new token to retry the held change." even when a token was entered.
This is pre-existing and has no effect on state.

Nothing was pushed, and no tracked file was modified apart from this report.
