# Independent closure review 4: commit 63b2232 (review-3 findings DC-005, refused renewal, DC-006)

- **Reviewer:** an independent agent. It changed nothing under `src/` or `tests/` and committed or pushed nothing.
  This file is the only file it wrote in the repository.
- **Date:** 2026-10-23. **Working tree:** HEAD `6379358`. `git diff 63b2232 HEAD -- src tests` is empty, so the
  reviewed code is the code at `63b2232`.
- **Scope:** `git diff afc3703 63b2232 -- src tests`. That covers `src/engine/Session.fs` (new `OpRefreshCompare`),
  `src/engine/Shell.fs` (`onRefresh`, `onEffect` routing, `auth.renew`), `src/kernel/main.js` (grace timer), and tests
  23, 23b and the new boot spec case. I also read the surrounding code: `onWrite` (verify, reconcile and rebase paths),
  `refresh`, `View.fs` (form projection, `actionable`), `GitHub.compare`/`parseCompareStatus`, and the test harness.

## What was run

1. `dotnet test tests/App.Engine.Tests -c Release` (after sourcing `env.sh`) → **Passed 53, Failed 0**.
2. `npm run -s check` → exit 0, with no output.
3. `npx playwright test tests/browser/boot.spec.js` → **6 passed** (29.6 s). This includes the new "fails once" case
   (1.4 s) and the persistent case (12.6 s).
   - `dist/site` was already present and was not rebuilt.
   - `dist/site/main.js` is byte-identical to `src/kernel/main.js` (checked with `cmp`).
   - Note: `dist/site/build-info.json` names `sourceCommit f83222b`. Its `src` differs from 63b2232 in three files, so
     the shipped *engine* in `dist/site` may predate 63b2232. That does not affect item 3, which is kernel-only. Item 1
     and item 2 were judged from the .NET tests and probes, not from the browser.
4. **Engine probes Q1–Q5.**
   - These were xunit facts in a throwaway project at `…/scratchpad/probe5/fs`, outside the repository.
   - The project referenced `src/engine/App.Engine.fsproj` and unmodified copies of `FakeGitHub.fs` and `Harness.fs`,
     plus `Fixtures.fs` with only its data path made absolute.
   - It also contained the scenario helpers from `EngineTests.fs` lines 1–88 and `pumpHolding`.
   - The probes print what they observe.
5. **Browser probes B1–B4.** These ran in Playwright with `/opt/pw-browsers/chromium-1194` against `dist/site`, served
   on port 4399 by `scripts/serve.mjs`. The config and spec were in `…/scratchpad/probe5/pw`.
6. Both probe directories were deleted afterwards. `git status --short` is empty.

## Summary

| Item | Verdict | Worst remaining variant |
| --- | --- | --- |
| 1. DC-005 reload regression | **OPEN** | **Medium** (Q1, Q2b): the automatic reload after a *moved or rebased* save still rolls the view back past the verified save. This is review-3 repro P6, and it is unchanged. **Medium** (Q3, new): a deliberate rollback by someone else is never adopted, and the next save silently re-publishes the rolled-back commits. Low (Q4): a compare that GitHub cannot answer makes every reload fail until reconnect. |
| 2. Refused renewal during a reload | **CLOSED** | None found. A cosmetic note only (below low). |
| 3. DC-006 grace timer | **CLOSED** for the stated claim | **Low–medium** (B1): on a slow connection, a transient failure that the loader recovers still fails the boot if the rest of the download takes more than 10 s after that failure. Low (carried over from review 3): after a failure, the kernel keeps starting behind the failure screen. |

---

## 1. DC-005 reload regression: OPEN

### What is correct

- **The compare direction is right.** `GitHub.compare t base head` requests `/compare/{base}...{head}`
  (`src/engine/GitHub.fs:125`). The reload calls it as `compare live.Target live.Head.Commit commit`
  (`src/engine/Shell.fs:307`), so base = the shown head and head = the ref read.
  - `"behind"` means the ref read is an ancestor of the shown head. That is a stale replica, or a rollback.
  - `"ahead"` means the ref read contains the shown head, so it is genuinely newer.
  - `"diverged"` means someone rewrote the branch. That case and any unexpected status are adopted (`Shell.fs:319`),
    which is the conservative choice.
- **Routing.** `refresh` and `onRefresh` emit through `emit`, which uses generation 0.
  - `onEffect` routes `OpRefreshCompare` to `onRefresh` only when `generation = 0 && live.Refreshing`
    (`Shell.fs:529`).
  - A late answer after a disconnect is dropped, because `Inflight` is cleared or the connection is no longer
    `Connected`.
  - A write's answers carry generation ≠ 0, so they cannot reach `onRefresh`.
- **`Refreshing` cannot stay true.** Every branch of `onRefresh` either emits exactly one next request or clears
  `Refreshing`:
  - any provider error goes to `stop` (`Shell.fs:300`);
  - a parse error goes to `stop`;
  - `"behind"` clears it (`:310-317`);
  - `OpTree` clears it (`:327`).
  - `Op`s outside the routed four never reach `onRefresh`.
  - Probes Q4 and Q5 checked the 404, network and unknown-status cases: each ended with `refreshing=false`.
- **The pending re-apply is handled on `"behind"`.** It becomes `Held(w, Conflict …)`, so it is visible and stays
  re-appliable (`:312-314`).
- **A genuinely newer head is still adopted.** Test 23 covers this in its second half, and it passes.
- **Review-3 repro P2 is fixed.** The manual reload after a *consistent* save works, because `Head` was advanced to
  the verified commit (`Shell.fs:487-497`). Test 23 covers it.

### Remaining variant Q1/P6 (medium): the automatic reload after a moved save still rolls back

The new guard compares against `live.Head.Commit`. After a save with `Moved = true` (another writer appended during
verification, or the save was rebased), `Head` is deliberately **not** advanced. It stays at the pre-write read point
(`Shell.fs:487-493`, `Head = (if consistent then head else live.Head)`). `refresh` is then issued automatically
(`Shell.fs:500`).

Suppose a lagging replica answers that automatic reload's ref read with the pre-write head. Then
`commit = live.Head.Commit` takes the equality branch (`Shell.fs:305`). No compare is made, and the catalog is rebuilt
from the old tree.

Review-3 repro P6 is therefore **unchanged** by this commit.

**Repro Q1** (helpers from `EngineTests.fs`; `refBody sha` is the GET ref JSON naming `sha`):

```fsharp
let w0 = connected "q1" (workspaceRepo "q1")
let h0 = w0.Repo.Head
w0 |> withRule isGetRef (Concurrent(otherWriterAppends prefix openIds[1] "after patch"))
   |> withRule isGetRef (Respond(200L, refBody h0))
   |> createInvestigation "Auto" "x"
```

Observed:
`sync=Idle refreshing=false head=a277658 (=h0) repoHead=f442ca8 catalog=5 lastVerified=41a6d7e`. The saved
investigation has gone from the catalog, and `headLabel` shows h0.

### Remaining variant Q2b (medium): the automatic reload after a rebased save rolls back

This is the "automatic reload after a rebased save" from the brief.

1. The first PATCH meets another writer's commit `mid`. The engine rebases onto `mid`, and the save verifies.
   `Moved = true` and `Head` stays at h0.
2. The automatic reload's ref read is answered by a replica that still names `mid`.
3. `compare h0...mid` is `"ahead"`, so `mid` is adopted (`Shell.fs:319`). `mid` is older than our verified commit.

**Repro Q2b:**

1. `w0 |> withRule isPatch (Concurrent(otherWriterAppends prefix openIds[1] "other"))`, capturing `mid`.
2. Open new-investigation, fill it, and deliver `cmd.submit`.
3. `pumpHolding` the **3rd** GET ref. The first is the rebase reload, the second is verify, the third is the automatic
   reload.
4. Answer that held effect with `200 refBody mid` and pump.

Observed:

| Moment | State |
| --- | --- |
| Before the answer | `Idle refreshing=true head=e88fdea catalog=6 lastVerified=a57983c` |
| After the answer | `Idle refreshing=false head=2000e03 (=mid) repoHead=a57983c catalog=5`. The saved investigation is missing. |

No remote data is lost in Q1 or Q2b. The symptom is the one closure reviews 2 and 3 rated medium: the verified save
disappears, and the next decision is parented on a stale head.

**Fix direction.** Anchor the monotonic check on the newest commit the engine *knows* to be on the branch. When
`LastVerified = Some(c, _)` and the ref read ≠ `c`, compare `c...ref`:

- `"behind"` or `"identical"`-to-older → keep;
- otherwise adopt.

Alternatively, advance a separate "known minimum" sha on every verified save, including moved ones. Add Q1 and Q2b as
tests. Test 23 only exercises the consistent-save case.

### Remaining variant Q3 (medium, new in this commit): a deliberate rollback is never adopted, then undone by the next save

"behind" is treated as a lagging replica forever. Nothing re-reads with a new nonce, and nothing ages out the
decision. So when another user deliberately force-resets the branch to an ancestor, two things happen:

- this client never shows the rollback, however often it reloads;
- its next save is parented on the newer, rolled-back commit. That is a fast-forward from the reset ref, so the
  non-forced PATCH succeeds and **re-publishes the commits the other user removed**.

Before 63b2232, a reload adopted the rollback.

**Repro Q3:**

1. `saved = connected "q3" … |> createInvestigation "First" "x"`, with `c1 = saved.Repo.Head`.
2. `rolled = { saved with Repo = { saved.Repo with Head = h0 } }`.
3. `rolled |> fire "refresh" |> fire "refresh"`. Both reloads give `head=de436ad (=c1) repoHead=c1720b5 (=h0)` and the
   notice "…the newer state is kept. Reload again in a moment."
4. `|> createInvestigation "Second" "y"` gives `Idle head=18dd4c2 catalog=7`, "Saved to GitHub and verified". Then
   `GET /compare/c1...head` on the fake returns `{"status":"ahead"}`, so the rolled-back c1 is back on the branch.

The only recovery is disconnect and reconnect.

**Fix direction.** On `"behind"`, re-read the ref once with a new nonce. If the second read still names the same
ancestor, or if the kept head is older than a short window since `LastVerified`, adopt it. This bounds the "keep"
decision to the replica-lag window that motivated it.

### Remaining variant Q4 (low): a compare GitHub cannot answer makes every reload fail

GitHub answers `404 "No common ancestor between …"` for unrelated histories, for example after the branch was
re-created with an orphan commit. It also answers 404 when the base commit is no longer available.

`stop` then reports "Reload failed: The repository, branch or object was not found…". `Head` never changes, so
**every** following reload asks the same compare and fails the same way.

**Repro Q4:** on a connected world, move the branch, then `withRule isCompare (Respond(404L, "{\"message\":\"No common
ancestor between a and b.\"}")) |> fire "refresh"`, twice. Both give the same "Reload failed" notice with
`refreshing=false`.

Reconnecting recovers. **Fix:** treat a 404 or 422 from the reload compare as "not an ancestor" and adopt the ref.

### Other paths checked (no finding)

- A compare network error → `stop`, "Reload failed: The network is unavailable…", `refreshing=false` (Q5).
- An unknown status string → the ref is adopted. Then `getCommit` on the (fake, nonexistent) sha fails and the reload
  stops cleanly (Q5).
- The re-apply path (`sync.reapply`): `Head` is the pre-conflict read point and the real ref is newer, so the answer is
  `"ahead"` and the ref is adopted. A stale `"behind"` turns the re-apply into a visible held conflict.

---

## 2. Refused renewal during a reload: CLOSED

**Evidence:**

- `Shell.fs:622-624`: when `live.Refreshing` and `Sync = Held _`, the code returns
  `plain (app |> App.notice Attention …)`. It uses the original `app`, not `form`, so:
  - `ConnectForm["token"]` is kept;
  - `live.Token` is unchanged;
  - no effect is emitted.
- Test 23b asserts both (old token kept, typed token still in the form). It passes. The guard comes before the
  `WriteDenied` and `Unknown` branches, so neither can install the token during a reload.

**Leak check:**

- The token is never projected. The view reads `owner`, `repo`, `branch` and `root` from `ConnectForm`, but not
  `token` (`View.fs:92-105`).
- `ConnectForm` is never written to Storage. No Storage effect references it.
- The token appears only in the `Authorization` header built from `live.Token`, never in a URL.
- Both renewal inputs are `type="password" autocomplete="off"` (`src/kernel/index.html:173, 273`).

**Later use.** The kept value is consumed only by the next `auth.renew`, which is the intended "press again after the
reload" behaviour, or removed by `disconnect` (`Shell.fs:109`) or `connect.submit` (`:93`). It cannot be sent with a
connect, because a connect requires `Disconnected`, and a disconnect clears it.

**Cosmetic note (below low, not reachable from the UI):** during a re-apply's reload, `Sync` is `Idle` with
`Reapply = Some`, not `Held`. So the new guard does not match: `auth.renew` falls to `_`, removes the typed token and
says "Enter a new token…". `canRenew` is false then, and nothing is written.

---

## 3. DC-006 grace timer: CLOSED for the stated claim

**Evidence:**

- `main.js:101-112`: every rejection calls `preventDefault`, then logs `event.reason` through `console.warn`. That
  closes the review-3 "evidence hidden" low. It then clears the timer and re-arms a 10 s `grace` timer, which rejects
  `loaderFailure`.
- The `finally` block (`:123-127`) clears `timer` and `grace` and removes the listener on every exit:
  - success;
  - a `kernel.start()` rejection;
  - a timeout;
  - a grace expiry.
- A rejection after the race has settled cannot arm a timer, because the listener has already been removed. A grace
  timer that would fire after a successful start has already been cleared.
- An unsettled `timeout` or `loaderFailure` promise holds no resources once its timer is cleared.
- `npm run -s check` passes, so `main.js` type-checks under `checkJs` and `strict`.

**Browser probes:**

| Probe | Result |
| --- | --- |
| B4: a rejection after ready is not swallowed | `boot=ready`, then `pageerrors=["after-boot"]`. |
| B2: an unrelated rejection at 300 ms during a healthy start | `boot=ready` at 773 ms. Review 3 had `boot=failed` here. |
| Repository test "fails once" (503 once, then OK) | Ready in 1.4 s, with ≥ 2 attempts: the loader's own retry succeeds. |
| B3: persistent 503 | `boot=failed` at 10.7 s, `hits=3` (the loader's two retries), `#causa-app` still hidden 3 s later. Far below the 90 s timeout. The repository's persistent-case test also passes (12.6 s). |

### Remaining variant B1 (low–medium): a recovered transient failure on a slow connection still fails the boot

The grace period counts from the **last rejection**, not from the last loader progress. If the loader's retry
succeeds but the rest of the download still needs more than 10 s, the start is aborted although it would complete.

**Repro B1 (Playwright):**

1. Route `System.ComponentModel.EventBasedAsync.*.wasm` to answer 503 on the first hit and pass through afterwards.
2. Route `dotnet.native.*.wasm` with a 15 s delay. That stands in for a slow connection.

Observed: `boot=failed ms=10257 hits=2`. The retry ran and succeeded. The control without the 503, with the same
delay, gives `boot=ready ms=15458`.

The 503-during-propagation scenario on a slow mobile connection is therefore still not absorbed, although it is now
limited to slow connections. **Fix direction:** either option works.

- Re-arm the grace timer while the loader is still making progress. For example, a `PerformanceObserver` on
  `resource` entries under `/_framework/` restarts it.
- Fail only on the loader's terminal rejection (`download '…' failed`), with the 90 s timeout as the backstop.

### Remaining variant (low, carried over from review 3, not addressed)

After a `"loader"` failure, `kernel.start()` keeps running, and it can later bind the document behind the hidden
`#causa-app`. Ignore a late resolution after `fail()` has run.

---

## Recommended actions

1. **DC-005 (medium):** anchor the monotonic reload on `LastVerified`, not only on `live.Head`, so that moved and
   rebased saves are protected. Add Q1/P6 and Q2b as tests.
2. **DC-005 (medium):** bound the "keep newer" decision: re-read once with a new nonce, or adopt after a short window,
   so that a real rollback is shown and not undone by the next save (Q3).
3. **DC-005 (low):** treat a 404 or 422 compare on reload as "not an ancestor" and adopt the ref (Q4).
4. **DC-006 (low–medium):** extend the grace period on loader progress, or fail only on the terminal "download …
   failed" (B1).
