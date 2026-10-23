# Closure review 7: atomic GraphQL save (eb7e12e)

- Reviewed SHA: `eb7e12e` (diff against `794f886`).
- Scope: `src/engine/{GitHub,Session,Shell,View}.fs`, the test fakes, and DC-003 and DC-005 (last sections).
- Nothing tracked was modified. The probes ran in a scratch worktree at `eb7e12e`, which was removed afterwards.

## What was run

The scratch worktree was
`/tmp/claude-0/-home-user/665f3fcc-18ff-5e63-937e-80d2d01e3679/scratchpad/r7`, with
`CAUSA_DATA_DIR=/home/user/causa-data`. It ran `dotnet test tests/App.Engine.Tests -c Release`.

- **Baseline:** all 62 existing tests pass.
- **Probes:** four probes (R7P1–R7P4) were appended to `EngineTests.fs`. All four fail as described below, so each
  finding marked "proved" was reproduced by a test.

## Verdicts

| Invariant | Verdict |
| --- | --- |
| DC-005 / (1) Safety: no publication over a head the engine did not decide on, and no overwrite of another writer's change | **CLOSED** (one low residual, F6) |
| DC-003 / (2) A sent save is never reported as "not written" without a refusal GitHub stated | **OPEN** (F1, F2, F3, F4) |
| DC-002 / (3) Answers apply only to their own question; events act only in legal states | **CLOSED** |
| (4) An unknown outcome is reconciled correctly (applied save recognised; resends bounded) | **OPEN** (F2 and F4: an applied save is reported as a conflict. Resends are bounded.) |
| (5) Success is projected only after independent read-back | **CLOSED** |
| (6) GraphQL handling matches GitHub's API | Request shape **correct**. Response classification **OPEN** (F3, F7). |
| (7) Tokens are never logged or persisted | **CLOSED** (unchanged; the token appears only in the Authorization header) |
| DC-006 | Untouched; no regression seen |

### Why DC-005 is closed

**Every publication is guarded.** There is exactly one write builder, `GitHub.commitOnBranch` (`GitHub.fs:131-167`).
Every caller passes `w.BaseCommit` as `expectedHeadOid` (`Session.fs:233-236`). GitHub evaluates that guard at write
time, so no stale read and no race can apply a save over a head other than `BaseCommit`.

**`BaseCommit` is only ever set to a head a decision was made on.** There are three places:

| Where | Set to | Why that head was decided on |
| --- | --- | --- |
| `decide` (`Shell.fs:187`) | `live.Head` | The catalog the decision was read from |
| `reapply` (`Shell.fs:233`) | The refreshed `live.Head` | The investigation was loaded from that same listing |
| Rebase (`Shell.fs:433`) | The `head` read back | Only when this investigation's entries at that immutable commit equal `BaseEntries` |

Retry, renewal and reconcile keep `BaseCommit`.

**A stale head can only cause a refusal, never a wrong write.** A stale replica answering the reload gives an older
`head`, and GitHub refuses the save (`STALE_DATA`). A reset is refused the same way.

**Another writer's change to the same investigation is never overwritten.** The rebase requires the investigation to
be unchanged. The guard then pins the write to that exact commit.

### Why DC-002 is closed

- Every publication and read carries the write's generation, and answers are matched on correlation id
  (`Shell.fs:500-512`).
- `sync.reconcile` and `auth.renew`-on-Unknown mint a new generation.
- Retry and renewal go through `startWrite`, which also mints a new generation.
- A refresh is refused while a save is in flight, and a save is refused while a refresh runs.
- Only one publication is outstanding at a time.

### Why (5) and the request shape hold

**Read-back (5).** The only Positive "Saved" projection is in `OpVerifyBlob` (`Shell.fs:455-493`). It is reached
after commit, tree, byte-equal blob reads and re-validation. A save found applied (`now = expectedShas`) also goes
through that verification.

**Request shape (6).** The body is
`{query, variables:{input:{branch:{repositoryNameWithOwner, branchName}, expectedHeadOid, message:{headline, body}, fileChanges:{additions:[{path, contents(base64)}]}}}}`.
It is POSTed to `https://api.github.com/graphql`, which the page's CSP `connect-src` allows.

**HTTP-status layer for `/graphql`.**

| Answer | Classified as | Result |
| --- | --- | --- |
| 200 | The body is parsed | — |
| 401 | `Unauthorized` | A stated refusal |
| 403 or 429 | `Forbidden` | A stated refusal |
| 404 | `NotFound` | A stated refusal |
| Other 4xx | `Rejected` | A stated refusal |
| 5xx | `Rejected` with status ≥ 500 | Unknown (correct for a 502/504 GraphQL timeout) |
| A network failure, `Cancelled` or a timeout | — | Unknown or reconciled, never NotSent at the Committing stage |

## Findings

### F1 (high, DC-003): an applied save is reported "Not saved (offline). Nothing was sent." Proved by R7P1

**Root cause.** `afterSend` treats the `Reloading*` stages as "nothing sent yet" (`Shell.fs:378-381`). That was true in
the REST design, where a reload followed only a stated refusal or a write in which no commit had been created.

In this commit, `OpReconcileRef` also enters `ReloadingCommit` when the ref differs from the base (`Shell.fs:409` →
`reload`, `:392`). That is exactly the case "our unknown save may have been applied". From there, any failure goes to
`holdFor`. With `w.Commit = None` (it is always `None` after an unknown outcome), a network failure becomes `NotSent`
(`Shell.fs:247-248`), and a 5xx or a malformed answer becomes `WriteFailed`.

**Reproduction:**

1. Submit an observation.
2. The GraphQL save is applied, but its answer times out (`OutcomeUnknown`).
3. Reconcile reads the ref, which now names our new commit.
4. `GET /git/commits/<new>` fails with a network error.

The engine holds `NotSent`. The view reads "Not saved (offline) — … The network is unavailable. Nothing was sent."
`sync.discard` would say "nothing was written".

**Mitigation.** `sync.retry` is safe: the guard refuses, and the reload recognises the change. The probe showed one
copy and `Idle` after the retry. So no data is damaged, but DC-003's rule is broken outright.

The same applies to a `STALE_DATA` refusal that follows an earlier unknown attempt (a reconcile resend), whose reload
then fails.

**Fix direction.** Record on the `Write` that some send's outcome was unknown, for example a `MaybeApplied` flag set
on `OutcomeUnknown` and on any `afterSend` failure. While it is set, treat every failure as Unknown and never as
NotSent or WriteFailed.

### F2 (medium, DC-003 and invariant 4): an applied save, followed by another writer's change, is reported "Your change was not written". Proved by R7P2

**Root cause.** After an unknown outcome there is no commit oid, so applied-recognition is only
`now = expectedShas w` (`Shell.fs:427`). If another writer changed the same investigation after our save landed, the
tree matches neither our files nor `BaseEntries`. That yields `Conflict "Someone else changed this investigation … Your
change was not written."` (`Shell.fs:435`).

**Reproduction:**

1. The save is applied, with its answer lost.
2. Before the reconcile ref read, another writer appends an observation to the same investigation.
3. Reconcile reads the new head and reloads it.

The engine holds `Conflict`, which says the change was not written, but it is on the branch.

**Follow-on.** `sync.reapply` re-decides the same envelope on top of history that already contains it. In the probe,
the domain refused with `DUPLICATE_ID`, and the notice said "… could not be re-applied … and was not saved", which is
false a second time.

Duplicate publication was not observed for `add-observation`, because the reused entity id is refused. It is not
proven impossible for commands that create no new ids, but no test was run for that case.

**Regression note.** The REST design recognised this case: it had the commit sha and could compare ancestry
(`OpReconcileCompare`, test 11d's intent). Test 11d still passes only because its concurrent change touches another
path.

**Fix direction.**
- While "maybe applied" is set, hold this case as Unknown rather than Conflict.
- Or recognise the save by looking for the envelope's `CommandId` in the head's `events.ndjson`. The correlationId is
  in each event.

### F3 (medium, DC-003 and invariant 6): an untyped GraphQL error in a 200 answer is treated as a stated refusal. Proved by R7P4

**Root cause.** `parseCommitOnBranch` maps any error that is not `STALE_DATA`, `RATE_LIMITED`, `FORBIDDEN` or
`NOT_FOUND` to `Rejected(200, msg)` (`GitHub.fs:251`). `fail` then treats `Rejected` below 500 at the Committing stage as
a refusal (`Shell.fs:387`) and holds `WriteFailed` ("Save failed … GitHub rejected the request (HTTP 200)").

GitHub's internal or timeout error, "Something went wrong while executing your query. This may be the result of a
timeout…", has no `type`. It does not state that nothing was applied, so the mutation may have run.

**Reproduction.** The save is answered with `200 {"data":null,"errors":[{"message":"Something went wrong while
executing your query. This may be the result of a timeout…"}]}`. The engine holds `WriteFailed`, and discard says
"nothing was written".

**Mitigation.** Retry is safe, because of the guard.

**Fix direction.** Classify only the known refusal types as refusals:
- `STALE_DATA`, `FORBIDDEN`, `NOT_FOUND` and `RATE_LIMITED`.
- Plus validation types such as `UNPROCESSABLE` and `INVALID` if desired.

Map an untyped error, or one of unknown type, to `OutcomeUnknown` and reconcile.

### F4 (low–medium, invariant 4): three consecutive stale ref reads turn an applied save into "The branch kept moving while saving". Proved by R7P3

**Reproduction:**

1. The save is applied, with its answer lost.
2. The reconcile ref read is a stale replica naming the base, so the save is resent. GitHub refuses (`STALE_DATA`).
3. The reload ref read is stale again (base), so the engine rebases onto the base and resends. GitHub refuses.
4. This repeats until `Attempts ≥ maxRebases` (`Shell.fs:398`).

After 4 save requests, the engine holds `Conflict "The branch kept moving while saving."`, but the change is on the
branch. Re-applying is then refused with `DUPLICATE_ID` and the notice "was not saved". Nothing is overwritten, but an
applied save is shown as a conflict.

This needs repeated lagging ref answers, which the engine's own threat model (DC-005, review 4) assumes.

**Fix direction.** The same "maybe applied" flag as F1: hold Unknown, not Conflict, once any attempt's outcome was
unknown.

### F5 (low, liveness): once the resend bound is reached, an unconfirmed save can never be published again

**Root cause.** `OpReconcileRef` with `sha = BaseCommit` and `Attempts ≥ maxRebases` returns Unknown immediately
(`Shell.fs:407`). `Attempts` is never reset, `canRetry` is false for Unknown, and renewal on Unknown only reconciles.

**Effect.** If the save truly never arrived, the user can only discard. This is safe, but a dead end. Observed by
reading the code, and consistent with test 22d. No separate probe was run.

### F6 (low, DC-005 residual): an applied save that someone else then removed is re-published

**Reproduction:**

1. The save is applied, with its answer lost.
2. Before reconcile, another actor either force-resets the branch to exactly `BaseCommit`, or commits a revert that
   restores this investigation to `BaseEntries`.
3. In the first case, reconcile sees the base and resends under the same guard, which succeeds. In the second case,
   the reload sees `now = BaseEntries`, so the engine rebases and publishes.

Only our own change is re-added. No third-party commit removed by a reset is restored, and the publication applies on
a head the engine verified. With no commit oid the engine cannot tell "never applied" from "applied, then removed".
Recognising by `CommandId` (as in F2) would not detect this either.

Reasoned from the code; not probed.

### F7 (low, invariant 6): odd but valid responses

| Response | Current result | Assessment |
| --- | --- | --- |
| `data.createCommitOnBranch.commit.oid` present together with a non-empty `errors` (a partial result) | Classified by `errors[0]` (`GitHub.fs:243-251`), so an unknown type yields `WriteFailed` although GitHub reported a created commit. Proved in R7P4 (`partial=Error(Rejected(200,"x"))`). | An oid should be treated as authoritative, or at least as Unknown. |
| An oid with `"errors": null` or `[]` | Falls to `Malformed` and is held as Unknown (`:252`) | Conservative and acceptable; reconcile then recognises the change. |

## Notes (no finding)

- **Rebase and new investigations.** The rebase decision compares only this investigation's entries, at an immutable
  commit read by sha, so it is sound. A new investigation has `BaseEntries = Map.empty`, and that matches an absent
  directory, so it is correct.
- **Message fields.** The `headline` contains no newline (it is a command label plus id and version); the body is
  `"Command <uuid>"`.
- **Tests.** The C# and F# fakes model `STALE_DATA`, `FORBIDDEN` and `NOT_FOUND` with plausible GitHub messages.
  Live acceptance against real GitHub is still required (DC-005 says so).
