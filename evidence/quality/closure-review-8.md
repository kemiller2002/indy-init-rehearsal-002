# Closure review 8: "may have applied" and recognition by command id (43690a0)

- Reviewed SHA: `43690a0` (diff against `eb7e12e`, `src tests docs`).
- Scope: `src/engine/{GitHub,Session,Shell,View}.fs`, tests `26`–`26f`, DC-003 (last section) and DC-005.
- Nothing tracked was modified. The probes ran in a scratch worktree at `43690a0`, which was removed afterwards.

## What was run

The scratch worktree was
`/tmp/claude-0/-home-user/665f3fcc-18ff-5e63-937e-80d2d01e3679/scratchpad/r8`, with
`CAUSA_DATA_DIR=/home/user/causa-data`. It ran `dotnet test tests/App.Engine.Tests -c Release`.

- **Baseline:** all 68 existing tests pass, including `26`–`26f`.
- **Probes:** six probes (R8P1–R8P6) were appended to `EngineTests.fs`.
  - R8P1 fails as described under N1, so N1 is proved.
  - R8P2–R8P5 pass. They confirm the closures claimed below.
  - R8P6 is an observation probe. It printed its result and asserted nothing.

| Probe | What it set up | What it showed |
| --- | --- | --- |
| R8P1 | The save is applied but its answer is lost. Before reconcile, another writer deletes the investigation's directory. | Holds `Conflict "… Your change was not written."`. Discard then says "nothing was written". This is **N1**. |
| R8P2 | The first save is unknown and not applied. Reconcile reads the base and resends. The resend is refused with `STALE_DATA`. The reload's ref read fails. | Holds `Unknown`. This is the F1 variant. |
| R8P3 | The save is applied, then another writer appends. The recognition `events.ndjson` read fails. | Holds `Unknown` with `MayHaveApplied = true`. `sync.reconcile` then recognises the save: Idle, "was saved to GitHub", one copy. |
| R8P4 | The save is applied, then another writer adds `extra.txt` to the directory, so the directory becomes invalid. | Idle, with "Your change was saved to GitHub (found in the history …)". After the reload, the catalog shows the entry as `Invalid`. |
| R8P5 | The marker text is placed inside a canonical JSON string value. | No match, because JCS escapes the quotes. The exact marker matches. An uppercase UUID does not match. |
| R8P6 | The resend bound is reached, then `auth.renew` is fired while the hold is Unknown. | No new save request is sent, and the hold is Unknown again. |

## Verdicts per finding

| Finding | Verdict | Basis |
| --- | --- | --- |
| F1 (failure while reconciling reported as "not sent") | **CLOSED** | See F1 below. |
| F2 (applied save, then another writer, reported "not written") | **CLOSED** for the reported case. One narrow variant remains open: **N1**. | See F2 below. |
| F3 (untyped GraphQL error treated as a refusal) | **CLOSED** | See F3 below. |
| F4 (stale reads turn an applied save into "kept moving") | **CLOSED** | See F4 below. |
| F5 (dead end after the resend bound) | **CLOSED**, with one note | See F5 below. |
| F6 (an applied save someone removed is re-published) | **RESIDUAL** (low). The claim is accurate and bounded, but incomplete; see N1. | See F6 below. |
| F7 (oid alongside errors) | **CLOSED** | See F7 below. |

**F1: closed.**
- `MayHaveApplied` is set on every `OutcomeUnknown` save answer (`Shell.fs:406`, `:408`).
- It is also set on every Unknown hold, through `unknown` (`:384`).
- `fail` checks the flag before any other rule (`:388`).
- So every later failure is Unknown, whatever the stage: Reloading*, Recognising, or a refusal of a resend.
- R8P2 proves the `STALE_DATA`-after-resend-then-reload-failure variant. Test 26 proves the base case.

**Where `holdFor` can still be reached.** It runs only while the flag is false. That means the only sends so far were
refused outright (`STALE_DATA`) or reached `VerifyingCommit` with an oid. In those cases "nothing was written" is true.

**How the flag travels through each action:**
- `sync.retry` and the `WriteDenied` renewal start only from `NotSent`, `WriteFailed` or `WriteDenied`. Those holds are
  produced only while the flag is false.
- Reconcile and renew-on-Unknown keep the write, so they keep the flag.
- `reapply` resets the flag (`:234`). It runs only from a Conflict, and every Conflict that can carry `true` is the N1
  case. In that case the investigation is gone, so the reapply cannot proceed.
- `disconnect` drops the held write. This is pre-existing behaviour, not a regression.

**F2: closed for the reported case.**
- Test 26b covers it. R8P3 covers a recognition read that fails.
- The recognition read is `OpRecognise` (`Shell.fs:441-459`).
- It runs only when `now` differs from both `expectedShas` and `BaseEntries`, the flag is set, and `events.ndjson` is
  present.
- The not-found branch says "your change is not on the branch". That is true at that head. The earlier guarded send
  cannot land later unless the branch returns to its old base, which is the F6 class.

**F3: closed.** Only `STALE_DATA`, `FORBIDDEN`, `RATE_LIMITED`, `NOT_FOUND` and `UNPROCESSABLE` refuse
(`GitHub.fs:247-254`). Anything else maps to `OutcomeUnknown`, which is reconciled with the flag set (`Shell.fs:406`).
Tests 26d and 26e cover this.

**F4: closed.** With the flag set, a `NotFastForward` at the bound becomes Unknown, not "kept moving"
(`Shell.fs:402`). Reconcile then recognises the save. Test 26c covers this.

**F5: closed.** `sync.reconcile` resets `Attempts` (`Shell.fs:603`), so each click allows at most `maxRebases + 1`
sends. Test 26f covers this.

Note: renewing while the hold is Unknown does not reset `Attempts` (`:634`). After the bound, a renewal reads the ref
and holds Unknown again without resending (R8P6). This is not a dead end, because `sync.reconcile` is still offered
and resets the bound. It is only an inconsistency.

**F6: residual, accurate and bounded.**
- DC-003's description of F6 is correct. Recognition cannot tell "never applied" from "applied, then removed".
- The fix adds no new publish path, and every publish still carries `expectedHeadOid = BaseCommit`.
- The per-reconcile bound reset allows more resends, but each one is guarded and re-adds only this command's own files.

DC-003 names two removal forms, a force-reset and a revert. It omits a third: deleting the directory of an existing
investigation. That form does not re-publish; instead it produces a false message (N1).

**F7: closed.** `Some c, _ -> Ok c` (`GitHub.fs:242`). Test 26e covers this. A named commit still goes through the
full read-back before it is reported as saved.

## Verdicts per class

| Class | Verdict |
| --- | --- |
| DC-005 (safety) | **CLOSED** |
| DC-002 | **CLOSED** |
| DC-003 | **CLOSED for the review-7 findings.** One low residual message defect remains (N1), plus F6. |

**DC-005: closed.** The diff does not change `commitOnBranch`, `commitRequest`, or any assignment of `BaseCommit`.
`Recognising` reads but never publishes. No path applies a save over a head the engine did not decide on, and no path
overwrites another writer's change.

**DC-002: closed.**
- `OpRecognise` answers are matched on generation through the existing `InFlight` check (`Shell.fs:535`).
- The found path sets `Sync = Idle` before calling `refresh`, so the refresh is accepted. It also clears `Reapply`.
- `TreatWarningsAsErrors` is on, so the matches over `WriteStage` are proved exhaustive:
  - View: `View.fs:119-124`
  - Shell: `afterSend`, at `Shell.fs:380-382`
- `OpRecognise` is reached only through `onWrite`.

**DC-003: closed for the review-7 findings.** Details are under the per-finding verdicts above.

**Read-back and the "found in history" path.** This path reports "saved" without reading back the bytes. I judge it
honest and safe, for these reasons:
- The tone is Neutral, not the Positive "verified by an independent read-back". `LastVerified` and `Known` are not
  advanced.
- The claim is narrow: the command is in the history at a named commit. The tone and wording also say that the
  investigation changed afterwards.
- Under CONTRACT §5.4, a correlationId names one command group. The command id is engine entropy, unique to this
  write.
- The commit is atomic, so the presence of its events means the whole commit was applied.
- The workspace is then reloaded and validated through the normal reader.

The weak point is that `events.ndjson` itself is not validated before it is matched. A hostile or non-conforming
writer could forge or re-encode it. That is speculative and outside the threat model, so it is not raised as a finding.

**The substring match.**
- Events are written as JCS: sorted keys, no whitespace, and `"` escaped as `\"` (`Json.fs:113-146`). Readers require
  canonical lines (`Store.fs:131`).
- `correlationId` exists only at the top level of each event (`Events.fs:305`).
- The UUID is written as `ToString("D")`, which is lowercase. That is the same form as `Ids.text`.
- A user string cannot produce the marker (R8P5). A key that merely ends in `correlationId` cannot match, because the
  marker starts with a quote.
- A false negative needs a non-canonical file, and the reader refuses such a file anyway.

If `events.ndjson` at the investigation path is a tree or a submodule, the blob read fails. The engine then holds
Unknown, which is benign.

## New findings

### N1 (low, DC-003): an applied save whose investigation is then deleted is reported "Your change was not written". Proved by R8P1

**Root cause.** The recognition branch requires `now.ContainsKey "events.ndjson"` (`Shell.fs:441`). When the directory
of an existing investigation is gone (`now = Map.empty`, which differs from `BaseEntries`), or `events.ndjson` is
missing, the engine falls through to `:446`:
`Conflict "Someone else changed this investigation on GitHub after you loaded it. Your change was not written."`

That happens even though `MayHaveApplied` is true. Discard then says "nothing was written" (`Shell.fs:613`), and
re-applying reports "no longer in this workspace; your change was not re-applied".

**Reproduction:**
1. Observe on an open investigation.
2. The save is applied, but the answer is `OutcomeUnknown`.
3. Before the reconcile ref read, another writer deletes all files under `<prefix>/<id>/`.

The engine holds the Conflict above.

**Impact.** Only the message is wrong. Nothing is re-published and nothing is overwritten, and the change is no longer
on the branch. It is the same class as F6, but the outcome is a false "not written" rather than a re-publication. It
needs a third party to delete the investigation inside the reconcile window.

**Fix direction.** When `MayHaveApplied` is set and recognition is impossible, either:
- hold Unknown, or
- word the Conflict as "your change is not on the branch (it may have been applied and then removed)". In that case,
  discard's text should also depend on the flag.

Also list this case under DC-003's F6 residual.

### Note (no finding): renewing while the hold is Unknown keeps `Attempts`

This is described under F5. Renewing does not resend after the bound, but reconcile does. For consistency, consider
resetting `Attempts` on the Unknown renewal as well.
