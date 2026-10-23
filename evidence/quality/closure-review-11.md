# Closure review 11 (independent)

- Reviewed: `git diff 730eda8 2f8fb98 -- src tests` (HEAD `2f8fb98`, base `730eda8`)
- Scope: `src/engine/Shell.fs` (`reapply` keeps `MayHaveApplied`; `onWrite` `cause` replaces `GitHub.describe Offline`
  in both outcome-unknown texts) and `tests/App.Engine.Tests/EngineTests.fs` (tests `28`, `28b`, `28c`; helper
  `otherWriterAppends` refactored into `otherWriterRecords`).
- Method: read the delta and every consumer of `MayHaveApplied` at `2f8fb98` (Shell.fs only; Session.fs declares the
  field, and no request builder reads it). Ran the suite in a scratch worktree of `2f8fb98`. Ran the new tests against
  the `730eda8` and `5bc6e92` sources. Mutation-tested the `cause` change. Wrote three scratch probes (R11P1-R11P3,
  not committed). The scratch worktree has been removed. Nothing was pushed, and no tracked file was modified.

## Results

| Check | Result |
| --- | --- |
| `dotnet test App.slnx -c Release` at 2f8fb98 | 94/94 conformance, 74/74 engine, all pass |
| 2f8fb98 tests on 730eda8 sources (`~28`) | `28b` FAILS (`Assert.True` on the flag), `28c` FAILS ("Sub-string found"), `28` passes |
| 2f8fb98 tests on 5bc6e92 sources (`~28`) | `28`, `28b`, `28c` all FAIL (`28`: "Sub-string not found") |
| Mutation: revert `cause` on the flag-true `fail` line only (Shell.fs:396) | committed suite **still green**; caught only by probe R11P1 |
| Probe R11P1 (flag-true Offline on a re-applied write) | 2f8fb98: "The save may have been applied (the network connection failed.) ..."; 730eda8: "...(The network is unavailable. Nothing was sent.)" |
| Probe R11P2 (re-applied save lands, answer lost, other writer appends) | both commits: "Your change was saved to GitHub (found in the history at commit ...)" |
| Probe R11P3 (re-applied save, branch keeps moving in another investigation) | 2f8fb98: `Unknown "GitHub did not confirm the save after several attempts."`, then `sync.reconcile` saves and verifies; 730eda8: `Conflict "The branch kept moving while saving."` |

The claims in DC-003 hold as written: `28b` and `28c` fail on 730eda8, and `28` fails on 5bc6e92.

## Verdicts on review 10 findings

- **T1: CLOSED.** `28` reaches the domain-refused `reapply` branch with the flag set (DUPLICATE_ID from another
  writer's same-id observation). It asserts "could not be re-applied" and "may have been saved", and that "not
  saved" is absent. It fails on 5bc6e92.
- **R10-1: CLOSED.** The re-decided write keeps `MayHaveApplied`. `28b` shows the follow-on conflict and the
  discard text keep the "may have been saved" form. It fails on 730eda8.
- **R10-2: CLOSED.** Neither outcome-unknown text uses `GitHub.describe Offline` any more. `28c` covers the
  `afterSend` line only. The flag-true line is fixed but no committed test covers it (T2 below).

## Trace: every consumer of `MayHaveApplied` for a re-applied write (flag now kept)

| Consumer | Before (730eda8, flag reset) | Now (flag kept) | Assessment |
| --- | --- | --- | --- |
| `fail` (Shell.fs:396) | stated refusal (401/403/404/4xx) gives WriteDenied/WriteFailed; Offline before the send gives NotSent | always `Unknown` | Conservative and consistent with DC-003: the same command id may have landed once. An Unknown hold accepts a renewed credential and reconciles (DC-003, review 2), so a 401 is still recoverable. Reconcile resends only under the base-commit guard. |
| NotFastForward cap (Shell.fs:410) | `Conflict "kept moving"` | `Unknown "did not confirm ... after several attempts"` | R11P3: bounded (5 sends, the same as before), then one user `sync.reconcile` gives a fresh bound and saves. There is no loop: `Attempts` resets only on a user action. The text is over-cautious, because every re-applied send was a stated refusal. But it is not false, and the old Conflict text did not claim "not written" either. Acceptable. Info (I1). |
| OpReloadTree recognition (Shell.fs:449-457) | reached only if the re-applied send itself went unknown | also reached after a stated refusal when the investigation changed | See "False saved?" below. |
| OpReloadTree flag-true, no events (Shell.fs:453) | "Your change was not written." | "not on the branch now (it may have been saved and then removed)" | This is R10-1's fix. Correct. |
| `sync.discard` (Shell.fs:625) | "nothing was written." | "It is not on the branch now" | Correct. |
| `reapply` refusal text (Shell.fs:231) / `onRefresh` IsNew text (Shell.fs:348) | flag read before any reset / IsNew never re-applies | unchanged | Not affected by the delta. |
| `unknown`, OutcomeUnknown sets (390, 414, 416) | set true | set true | Unchanged. |

The flag is never read by `Session.commitRequest` or any other request builder. It changes texts and hold kinds,
never what is sent.

### False "saved"? (recognition by correlationId for a re-applied write)

The re-applied write keeps `Envelope.CommandId`. `Session.containsCommand` looks for `"correlationId":"<id>"` in the
head's `events.ndjson`. The new exposure is a re-applied send that GitHub refuses (STALE_DATA), followed by a reload
in which the investigation changed and the id is present. A valid history cannot hold two groups with one
correlation id: `Store.fs:156-160` rejects that (`correlationReused`), and the domain refuses re-deciding onto a
state that already holds the command's entity ids (DUPLICATE_ID). So if the id is found, exactly one group of this
command is on the branch. In the refused case, that group must be the earlier landing, restored or never actually
removed. Reporting "saved" is then true: the command is on the branch. The bytes are those of the first decision
(different sequence and `RequestedAt`), not the re-decided ones. The message already says "The investigation has
changed since" and reloads, so nothing misleading is shown. The case "appended to rather than removed" cannot reach
`reapply`: recognition would have found the id first, and the write would have ended as saved, not as a Conflict.
A hand-forged correlation id in someone else's event would give a false "saved". That risk predates the delta (the
same substring match applies to every flagged write) and is outside the threat model of these reviews.
**Not a finding.**

### DUPLICATE_ID relevance

DUPLICATE_ID is keyed on entity ids (`Decide.fs:356`), not on the command id. A re-decided command reuses the first
decision's entity ids, so re-applying onto a state that still holds the earlier landing is refused, either by the
domain or by `Store.append` (`correlationReused`). It can never double-write. In that narrow case, where the
landing reappeared between the Conflict and the reload, the refusal text says "it may have been saved earlier, then
changed or removed". Strictly, the save is on the branch. The text hedges and does not claim "not saved". It is
pre-existing (730eda8), and the delta does not change it. Info (I2), no action required.

## Per-class verdicts for this delta

- **DC-002 (no write replaced or discarded in flight): UNAFFECTED.** No new `startWrite` call site. `reapply` still
  calls `startWrite cleared rewritten`, which refuses while InFlight or Refreshing. The discard guard is unchanged.
- **DC-003 (never "not written" when it may have been written): IMPROVED.** The flag now follows the command id
  across re-decide. Every consumer moves toward "may have been applied" or "not on the branch now". The only cost is
  usability: an Unknown hold, resolved by one Confirm, where a Conflict or retryable hold appeared before (I1). That is
  the trade-off DC-003 prescribes.
- **DC-005 (atomic guarded save): UNAFFECTED.** No request, guard or ref read changed. The flag does not reach
  `commitRequest`. Reconcile still resends only while the branch names `BaseCommit`.

## Findings

### T2 (low, test gap): the flag-true `cause` change is untested

Shell.fs:396 (`| _ when w.MayHaveApplied -> unknown (... + cause f ...)`) can be reverted to `GitHub.describe f`
with the whole committed suite still passing. `28c` reaches only the `afterSend` line (Shell.fs:401), because a new
investigation has the flag false on its first send. Reproduction (probe R11P1): `landedThenReverted`, then
`withRule isSave NetworkDown |> fire "sync.reapply"`, then `syncDetail` must not contain "Nothing was sent". This
fails on 730eda8 and under the mutation, and passes on 2f8fb98. The same path is also reachable without re-apply:
any Unknown hold, then `sync.reconcile` while offline. **Proved.**

### I1 (info): a refused re-apply at the rebase cap now ends Unknown instead of Conflict

Probe R11P3 shows this. It is bounded and recoverable with one `sync.reconcile`. It is consistent with DC-003.
**Proved. No action required.**

### I2 (info, pre-existing): the refusal text when the earlier landing reappears

The text says "then changed or removed" although the command is on the branch. Narrow, and it hedges rather than
denies. **Reasoned, not reproduced. No action required.**

## Verdicts

T1 CLOSED, R10-1 CLOSED, R10-2 CLOSED. DC-002 UNAFFECTED, DC-003 IMPROVED (no new false "not written" and no false
"saved"), DC-005 UNAFFECTED. Tests: `28` fails on 5bc6e92, and `28b` and `28c` fail on 730eda8, as claimed. One low
test gap (T2: the flag-true `cause` line). Two infos. No blocking findings.
