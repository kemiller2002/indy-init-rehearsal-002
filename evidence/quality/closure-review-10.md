# Closure review 10 (independent)

- Reviewed: `git diff 5bc6e92 730eda8 -- src tests` (HEAD `730eda8`, base `5bc6e92`)
- Scope: two message changes in `src/engine/Shell.fs` (`reapply` refusal notice; `onRefresh` IsNew re-apply conflict)
  and tests `27b`, `27c` in `tests/App.Engine.Tests/EngineTests.fs`.
- Method: read the delta; enumerated every "not written / not saved / nothing was written / nothing was sent" text
  reachable from `Shell.fs` and `View.fs` at `730eda8` and the `MayHaveApplied` state possible there; ran the suite in
  a scratch worktree of `730eda8`; ran the `730eda8` tests against `5bc6e92` sources; mutation-tested the two
  changes; wrote scratch probes (R10P1-R10P3, not committed). Scratch worktrees removed; nothing pushed; no tracked
  file modified.

## Results

| Check | Result |
| --- | --- |
| `dotnet test App.slnx -c Release` at 730eda8 | 94/94 conformance, 71/71 engine, all pass |
| 730eda8 tests on 5bc6e92 sources (`~27`) | `27c` FAILS ("Sub-string found"), `27`, `27b` pass |
| Mutation: revert the `onRefresh` IsNew text | `27c` fails (shown above) |
| Mutation: revert the `reapply` text (line 231) | **whole suite still passes**; caught only by probe R10P1 |

## N2 verdict: CLOSED

- IsNew path (`Shell.fs:345-349`): with `MayHaveApplied = true` the hold now reads "...now exists and differs from
  yours (yours may have been saved, then changed); it was not re-applied." The "not written" text is kept only for
  `MayHaveApplied = false`, where no send can have landed. `27c` exercises it and fails on 5bc6e92.
- Domain-refused re-apply (`Shell.fs:228-232`): with the flag set the notice reads "could not be re-applied to the
  latest version (it may have been saved earlier, then changed or removed): <reason>"; "and was not saved" only when
  the flag is false. Path is reachable with the flag true: probe R10P1 (landed observation, reverted plus an
  append by another writer, then a third write records an observation with the same id, then `sync.reapply`) gives
  "Your pending change could not be re-applied to the latest version (it may have been saved earlier, then changed or
  removed): Observation id already exists. (DUPLICATE_ID)". With the line reverted, the probe fails. The fix is correct,
  but no committed test covers it (T1).

## Enumeration of "not written"-type texts (Shell.fs / View.fs at 730eda8)

| Location | Text | `MayHaveApplied` there | Verdict |
| --- | --- | --- | --- |
| Shell.fs:153 `decide` | "...already exists; nothing was written. Try again." | no Write exists yet (pre-publication refusal) | correct |
| Shell.fs:231 `reapply` | "and was not saved" | only in the `false` branch | correct (fixed) |
| Shell.fs:298, 343, 354, 375 | "...your change was not re-applied." | may be true | correct: states only that re-apply did not happen. A later discard follows the flag (Shell.fs:622) |
| Shell.fs:348 | "your new investigation was not written." | only in the `false` branch | correct (fixed) |
| Shell.fs:455 | "Your change was not written." | `else` after both `MayHaveApplied` branches, so false | correct for the flag, but see R10-1 |
| Shell.fs:623 `sync.discard` | "nothing was written." | `_` after `Unknown` and `w.MayHaveApplied`, so false | correct for the flag, but see R10-1 |
| Shell.fs:250-251 `holdFor` (NotSent / WriteFailed) | NotSent detail = `describe Offline` = "Nothing was sent." | `holdFor` is reached from `fail` only after the `w.MayHaveApplied -> unknown` guard, so false | correct |
| View.fs:128 | "Not saved (offline)" | NotSent hold only, so false (as above) | correct |
| Shell.fs:389-397 `fail` Unknown (via `GitHub.describe Offline`) | "The save may have been applied (The network is unavailable. Nothing was sent.)" | true | contradictory, see R10-2 |
| Shell.fs:127 `startWrite` | "nothing else was started." | about a second command, not the held one | correct |

## Per-class verdicts for this delta

- **DC-002 (no write replaced or discarded in flight): UNAFFECTED.** Both branches build the same states as before:
  `Connected cleared` with `[]` effects in `reapply`, and `Held(w, Conflict _)` with `Reapply = None` plus the same
  `emit reads` in `onRefresh`. Only the string changes.
- **DC-003 (may-have-applied reconciliation): UNAFFECTED.** `MayHaveApplied` is read, never written, by the delta.
  `afterSend`, `fail`, `unknown`, the rebase and Recognising logic are untouched.
- **DC-005 (atomic guarded save): UNAFFECTED.** No request, guard or ref read changed.
- **Message accuracy (the N1/N2 class): IMPROVED.** N2 is closed. Two pre-existing residuals of the same class remain
  (R10-1, R10-2).

## Findings

### T1 (low, test gap, introduced with this delta): the `reapply` text change is untested

`27c` asserts `DoesNotContain("not saved", noticeText)` after `sync.reapply`. But for a new investigation `onRefresh`
takes the IsNew branch, clears `Reapply` and never calls `reapply`, so this assertion cannot fail. Reverting
Shell.fs:231 to the 5bc6e92 text leaves the whole suite green. Reproduction: the R10P1 probe described above, which
needs an existing investigation, a landed send, a revert plus another writer's append, and then a same-id
observation by another writer before the re-apply. Suggest adding it as `27d`.

### R10-1 (low, pre-existing, outside the delta): the flag reset on a successful re-decide brings back "not written"

`reapply` rewrites the held write with `MayHaveApplied = false` (Shell.fs:236). If the earlier send landed and was then
reverted, re-applying it and hitting a second concurrent change produces "Your change was not written." (Shell.fs:455),
and discarding it produces "nothing was written." (Shell.fs:623). But this command (same `CommandId`) was written
once. Probe R10P2: the 27b setup, then `sync.reapply` with another writer appending on the save. Output: `flag=false
Someone else changed this investigation ... Your change was not written. | discard=Your pending change was
discarded; nothing was written.` Resetting the flag is correct for the resend logic. The user-facing texts need a
separate "an earlier send may have landed" marker that survives re-decide. Same class as N1/N2. Nothing is
re-published or lost.

### R10-2 (low/info, pre-existing): an Unknown hold detail says "Nothing was sent."

`fail` embeds `GitHub.describe f` in the Unknown text. For `Offline` after the send (flag true), the user sees "The
save may have been applied (The network is unavailable. Nothing was sent.) Confirm what GitHub received...". Probe
R10P3: `withRule isSave NetworkDown`, then create an investigation; `syncDetail` shows that string. The text
contradicts itself. Suggested fix: a send-stage description for Offline, e.g. "The network failed; the request may
have been sent."

## Verdicts

N2 CLOSED. DC-002, DC-003, DC-005 UNAFFECTED (the delta changes messages only). Tests: `27c` covers the IsNew path and
fails on 5bc6e92. `27b` passes on both commits (it is a kept probe, not a regression test). The `reapply` message
change has no covering test (T1, low). Pre-existing residuals R10-1 (low) and R10-2 (low/info) are in the same message
class. No blocking findings.
