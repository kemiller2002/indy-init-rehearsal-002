# Independent closure review 3: commit 645f3ca (review-2 items A, B, C, D and DC-006)

- **Reviewer:** an independent agent. It changed nothing under `src/` or `tests/` and committed or pushed nothing.
  This file is the only file it wrote in the repository.
- **Date:** 2026-10-23. **Working tree:** HEAD `afc3703`, the merge of #3. `git diff 645f3ca HEAD -- src tests` is
  empty, so the reviewed code is the code at `645f3ca`. An untracked directory, `evidence/acceptance/live-run-39986660673/`,
  was already present when the review started. I did not touch it.
- **Scope:** `git diff 3eb7995 645f3ca -- src tests`. That covers `src/engine/Shell.fs`, `src/engine/View.fs`,
  `src/kernel/main.js`, `tests/App.Engine.Tests/EngineTests.fs` (tests 22, 22b, 22c) and `tests/browser/boot.spec.js`.
  I read `Shell.fs` in full, `src/kernel/main.js` in full, the relevant parts of `Session.fs`, `GitHub.fs`, `View.fs` and
  `index.html`, the vendored `dotnet-wasm-transport.js`, and the shipped `dist/site/wasm/_framework/dotnet.js`.

## What was run

1. `dotnet test tests/App.Engine.Tests -c Release` (after sourcing `env.sh`) → **Passed 51, Failed 0**.
2. `npm run -s check` → exit 0, with no diagnostics. `tsconfig.json` includes `src/kernel/**/*.js` with `checkJs` and
   `strict`, so `main.js` is type-checked.
3. **Engine probes.** These were six xunit facts (P1–P6) in a throwaway project at
   `/tmp/claude-0/-home-user/665f3fcc-18ff-5e63-937e-80d2d01e3679/scratchpad/probe3/fs`, outside the repository.
   - The project referenced `src/engine/App.Engine.fsproj` and unmodified copies of `FakeGitHub.fs`, `Harness.fs` and
     `Fixtures.fs`. Only the data path in `Fixtures.fs` was made absolute.
   - It also contained the scenario helpers from `EngineTests.fs` lines 1–85.
   - The probes print what they observe and assert nothing.
   - P4 and P5 use a pump that checks after every engine step that the engine is never `InFlight` while `Refreshing`.
     It reported no violation.
4. **Browser probes.** Playwright 1.64.1 with `/opt/pw-browsers/chromium-1194`, run against `dist/site`. I checked that
   `dist/site/main.js` is byte-identical to `src/kernel/main.js` at 645f3ca. The probes were served on port 4399 by
   `scripts/serve.mjs`, from a config and specs in `…/scratchpad/probe3/pw`. Some probes swapped in the pre-fix
   `main.js` from `3eb7995` through `page.route`, to compare the two.
5. Both probe directories were deleted afterwards.

## Summary

| Item | Verdict | Worst remaining variant |
| --- | --- | --- |
| A (DC-002): no write during a reload | **CLOSED** | Low (P1): a refused `auth.renew` still installs the new token. It was reached by sending the event directly; the UI hides the control. A related **medium** DC-005 residual (P2/P6), where a stale ref answer on a reload rolls back a verified save, is listed under C. |
| B (DC-004): actions hidden while reloading | **CLOSED** | None found. |
| C (DC-005): judging a differing post-PATCH ref read by compare | **CLOSED** for the stated claim: the direction is right and a diverged branch is never reported as success. | **Medium** (P2/P6): the claim that ref replicas lag is not applied to the *reload* path, so a lagging ref answer rolls the head and catalog back past a verified save. Low (P3): a force-reset to an ancestor right after the PATCH is reported "verified". This is acknowledged in test 22b. |
| D (DC-003): a rebase clears the pre-rebase commit | **CLOSED** | None found. |
| DC-006: start raced against the first `unhandledrejection` | **OPEN** | **Medium**: a *transient* failure of one framework file, which the .NET loader's built-in retry recovers on its own, now aborts the boot with the failure screen. This is a regression from 3eb7995. Low: `preventDefault` removes the only console record of the rejection, and an unrelated rejection during start aborts a healthy start. |

---

## A (DC-002): CLOSED

### Evidence

`startWrite` refuses while `live.Refreshing`, checked after `InFlight` (`src/engine/Shell.fs:125-128`). I checked every
way to start or resume provider writes:

| Path | Guard | Probe result |
| --- | --- | --- |
| `cmd.submit` | `Session.writeAllowed` refuses while `Refreshing` (`Session.fs:167`). `startWrite` refuses as well (`Shell.fs:210`). | Covered by existing tests. |
| `sync.retry` | `when not live.Refreshing` (`Shell.fs:577`), then `startWrite`. | P5: `Held NotSent effects=0 canRetry=false` during a reload. After the reload: `Idle`. |
| `auth.renew` on a `WriteDenied` hold | Through `startWrite` (`Shell.fs:608-610`): the closure-2 gap. | Test 22 passes. P1: `writes=0`, still `Held WriteDenied`. |
| `auth.renew` on an `Unknown` hold | `not live.Refreshing` (`Shell.fs:611`). | P5: `renew=Held Unknown eff=0`. |
| `sync.reconcile` | `when not live.Refreshing` (`Shell.fs:583`). | P5: `reconcile=Held Unknown eff=0`. |
| `sync.reapply` | Sets `Reapply` and calls `refresh`, which emits nothing while a reload runs (`Shell.fs:598-601`, `:113-117`). The re-apply starts only from `onBlob` with `not next.Refreshing` (`:344`). It goes through `reapply`, then `startWrite` (`:237`). | P5: `afterEvent=Idle eff=0 final=Idle lastVerified=true`. The invariant was never broken. |

The other direction is also closed: **a reload cannot start while a write is in flight.**

- `refresh` refuses on `InFlight` (`Shell.fs:115`).
- `sync.discard` and `sync.reapply` require `Held`.
- The automatic refresh after a moved save (`Shell.fs:499`) runs after `Sync = Idle`. It cannot overlap an older reload,
  because a write can no longer start during one.
- The routing at `Shell.fs:515` accepts generation-0 `OpRef`/`OpCommit`/`OpTree` answers only while `Refreshing`.
  Reload chains are sequential, so when `Refreshing` turns false none of their questions is still outstanding.
- Old-generation answers from a held write cannot reach `onWrite` (`Shell.fs:518`).

### Remaining variant P1 (low): a refused renewal still installs the new token

`auth.renew` builds `renewed = { live with Token = … }` and passes `{ form with Connection = Connected renewed }` to
`startWrite` (`Shell.fs:609-610`). When `startWrite` refuses because of the reload, it returns that app unchanged.
The result:

- the new token is installed;
- the typed token is removed from the form;
- the rest of the reload's requests use the new token.

**Repro (P1):**

1. `connected "q1" (workspaceRepo "q1") |> withRule isPostBlob (Respond(401L, …)) |> createInvestigation "R" "x"`.
2. `deliver (event "refresh" …)`, holding back the `GET commit`.
3. `send "connect.token" "ghs_SOMEOTHERTOKEN" |> fire "auth.renew"` gives
   `Held WriteDenied tokenSwapped=true formHasToken=false notice=The workspace is being reloaded… writes=0`.
4. Release the held `GET commit` and pump. The result is `Reload failed: GitHub did not accept the credential`.

**Reachability.** It is reachable only by sending `auth.renew` while `canRenew` is false: the template is removed then
(`index.html:173, 273`). A stale DOM or a race at the moment a reload starts could do it. No write happens.

**Fix:** return `form` instead of the renewed app when `live.Refreshing`. Keep the token in the form so that the user
can press the control again once the reload ends.

---

## B (DC-004): CLOSED

**Evidence:**

- `actionable` is `held hold && not Refreshing` (`src/engine/View.fs:136`).
- `canRetry`, `canRenew`, `canReapply` and `canReconcile` use it (`View.fs:143-146`).
- `canDiscard` still uses `held` (`:147`). That is correct: discard only drops the local change, and its `refresh` is a
  no-op during a running reload.
- Every `sync.*` and `auth.renew` control in `index.html` (lines 172-176 and 272-276) sits under the matching `data-if`.
- Test 22 asserts `canRenew = false` while reloading. P5 observed `canRetry=false` while reloading.

**Residual (UX, below low):** if the user is typing a renewal token when a reload starts, the input disappears. The
value already sent through `connect.token` stays in `ConnectForm`. When the input reappears it is empty, but pressing
the button uses the stored value.

---

## C (DC-005): CLOSED for the stated claim, with a medium residual on the reload path

### The direction of the compare is correct

- `GitHub.compare t baseSha headSha` requests `/compare/{base}...{head}` (`GitHub.fs:125`).
- The verify path calls it as `compare live.Target commit sha`, so base = ours and head = the ref read
  (`Shell.fs:441`).
- In GitHub's semantics, `ahead` means head contains base, and `behind` means head is an ancestor of base.
- The engine therefore acts as follows:
  - `identical`/`ahead` → verify, with `Moved = true`, so a refresh follows (`Shell.fs:400`). The ref contains our
    commit.
  - `behind` at `VerifyingRef` → commit read-back (`:403`). The ref is an ancestor of our commit.
  - Any other status at `VerifyingRef` → `Unknown` (`:404`). This covers `diverged` and any unexpected string.
  - A compare error → `fail` → `Unknown`, because `afterRefSent` includes `VerifyingRef` (`:357, :361`).
- The fake applies the same semantics (`FakeGitHub.fs`, compare route).

**Probes:**

- P4 "ahead" (another writer appended after our PATCH):
  `Idle lastVerified=true live.Head=11a493c repoHead=11a493c catalog=6`. The follow-up refresh moved `Head` to the tip.
- P4 "diverged" (the test 22b scenario): `Held Unknown lastVerified=false`. **No path reports success for a diverged
  branch.**
- Pressing `sync.reconcile` from that hold rebased and re-published onto the other line:
  `Idle lastVerified=true patches=ec253c4,9a6520c`. It used a non-forced PATCH and the user asked for it, so I find
  this acceptable. It is noted because the content had been removed deliberately by someone else's force-push.

### Residual P2/P6 (medium): a lagging ref answer on a reload rolls back a verified save

Commit 645f3ca's own premise is that a ref read can come from a lagging replica (`Shell.fs:401-402`). `onRefresh`
does not apply that premise. It replaces `Head` and `Catalog` with whatever the ref read names (`Shell.fs:302-315`),
even when that commit is an ancestor of the commit the engine has just verified.

The most exposed moment is the automatic refresh issued right after a moved save (`Shell.fs:499`), which is exactly
when a replica is most likely to lag. The symptom is the one closure review 2 rated medium under A:

- the saved investigation disappears from the catalog;
- `headLabel` goes back to the pre-write head;
- the next decision is parented on the stale head.

No remote data is lost.

**Repro P2 (manual refresh):**

1. `w0 = connected "q2" (workspaceRepo "q2")`, `h0 = w0.Repo.Head`.
2. `saved = w0 |> createInvestigation "Saved" "…"` gives `Idle head=2df69db catalog=6`.
3. `saved |> withRule isGetRef (Respond(200L, <ref body naming h0>)) |> fire "refresh"` gives
   `head=9a7540c (=h0, repo=2df69db) catalog=5 hasSaved=false lastVerified=2df69db`.

**Repro P6 (automatic refresh, no user action):**

`connected "q6" … |> withRule isGetRef (Concurrent(otherWriterAppends prefix openIds[1] "after patch"))
|> withRule isGetRef (Respond(200L, <ref naming h0>)) |> createInvestigation "Auto" "x"` gives
`Idle head=c5a77ed (=h0, repo=be20ec2) catalog=5 lastVerified=827e6b1`.

**Fix direction:** in `onRefresh`, when the ref read differs from `live.Head` (or from `LastVerified`), run
`compare live.Head...ref` first. If the answer is `behind`, keep the current view, or re-read once with a fresh nonce.
Add P2 and P6 as regression tests.

### Residual P3 (low, acknowledged): a reset to an ancestor right after the PATCH is reported verified

**Repro:**

1. `connected "q3" … |> withRule isGetRef (Concurrent(fun r -> { r with Head = before })) |> createInvestigation …`.
2. The result is `Idle lastVerified=true live.Head=6a80cc7 repoHead=c1720b5 ourCommitOnBranch=false` with the notice
   "Saved to GitHub and verified…".

Test 22b's comment records this as indistinguishable by a single read. This requires someone else to force-push inside
the verify window. A second ref read with a new nonce before accepting `behind` would make the window negligible.

---

## D (DC-003): CLOSED

**Evidence:**

- The rebase now sets `Commit = None` (`Shell.fs:428`).
- After a rebase, `OpReconcileRef` follows one of two branches:
  - with `sha = BaseCommit` and `Commit = None`, it republishes from the blobs (`:392`);
  - with any other sha, it reloads (`:397`).
- The pre-rebase sha can no longer reach `updateRef`.
- `holdFor` on `Offline` with `Commit = None` says "Nothing was sent" (`:250`, `GitHub.fs:302`). That is accurate for the
  branch: the earlier PATCH was refused as non-fast-forward.
- Test 22c checks this: no sha is PATCHed twice, and no PATCH is forced. It passes.

---

## DC-006: OPEN

### What is correct

- **No listener leak.** The listener is added before `kernel.start()` and removed in `finally` (`main.js:106, 118`),
  which runs on every exit from the `try`.
- **The losing promises are handled.** `Promise.race` attaches handlers to `timeout` and `loaderFailure`, so their
  later rejection is handled. A `kernel.start()` rejection after another branch has won the race is handled the same
  way.
- **A rejection after a successful start is not swallowed.** Browser probe: `AFTER boot=ready pageerrors=["after-boot"]`.
- **Typing is clean.** `npm run -s check` passes, and the JSDoc `(event: PromiseRejectionEvent) => void` matches the
  `addEventListener("unhandledrejection", …)` overload.
- **No competing .NET handler.** The .NET loader's own handler (`installUnhandledErrorHandler`) is installed only by
  `withExitOnUnhandledError()`. `dotnet-wasm-transport.js` does not call it, so `main.js` is the only listener.

### Remaining variant (medium): a transient download failure that .NET would recover now fails the boot

The shipped `dotnet.js` sets `enableDownloadRetry: true`. The relevant code in `dist/site/wasm/_framework/dotnet.js` is
`se`/`ae`, with the strings "Retrying download" and "Retrying download (2) … after delay". It retries a failed asset
twice, the second time after 100 ms.

Each failed attempt still raises an unhandled rejection ("Failed to fetch"). The new handler fails the boot on the
**first** of them, before the retry can run.

Browser probe: the route for `System.ComponentModel.EventBasedAsync.*.wasm` answers 503 once, then passes through.

| `main.js` | Result |
| --- | --- |
| 3eb7995 (old) | `boot=ready hits=2 ms=1008 pageerrors=["Failed to fetch"]`. The retry recovered on its own. |
| 645f3ca (new) | `boot=failed hits=1 ms=443`. The failure screen appeared and the retry never ran. |

With a persistent 503, the old kernel showed the DC-006 hang: after 20 s, `boot=null hits=3`. Its page errors were
three × "Failed to fetch", then two × "download '…EventBasedAsync…wasm' … failed 0 TypeError: Failed to fetch". The
new kernel fails at once.

So the fix trades a 90 s spinner on *persistent* failures for a hard failure on *transient* ones. A transient 503 during
CDN propagation, which is the scenario DC-006 describes, is exactly the case the loader used to absorb.

**Fix direction.** Do not fail on the first rejection. Either option works:

- on a rejection, start a short grace timer (a few seconds, longer than the loader's retry schedule), and fail only if
  `kernel.start()` has not settled by then;
- fail only on the loader's terminal rejection, whose message begins `download '` and ends with ` failed …`.

Add the "503 once, then OK → ready" case to `tests/browser/boot.spec.js` next to the persistent case.

### Remaining variant (low): `preventDefault` hides the evidence

`event.preventDefault()` (`main.js:102`) suppresses the browser's "Uncaught (in promise)" report, and the reason is
never logged. In the probes, the new kernel had `pageerrors=[]` where the old one recorded the failure. The SRI and 503
resource errors are still logged by the browser, but the loader's own message is not.

**Fix:** `console.error(event.reason)` before `preventDefault()`, or do not call `preventDefault`.

### Remaining variant (low): an unrelated rejection aborts a healthy start

Any page rejection during the start window ends the boot with "Part of the application could not be downloaded".

Probe: an init script calls `Promise.reject(new Error("unrelated"))` at 300 ms. The result is `boot=failed`. Eight
seconds later, `#causa-app` was still hidden while the engine had rendered 62 elements into it: the kernel kept running
behind the failure screen.

Nothing in the shipped page is known to reject during start, so this is low. Two improvements:

- the grace timer above, or filtering on the reason, also covers this case;
- after a failure, the kernel should not keep binding the document behind the failure screen, for example by ignoring
  a late `kernel.start()` resolution.

---

## Recommended actions

1. **DC-006 (medium):** do not fail on the first `unhandledrejection`. Use a grace timer, or match the loader's
   terminal "download … failed" rejection. Log `event.reason`. Add a "503 once → ready" browser test.
2. **C/DC-005 (medium):** in `onRefresh`, refuse to move `Head` backwards. Compare against the current head, re-read
   once on `behind`, and add P2 and P6 as tests.
3. **A (low):** on a refused `auth.renew`, return the app unchanged: keep the old token and keep the typed token in the
   form.
4. **C (low):** on `behind` at `VerifyingRef`, re-read the ref once with a new nonce before accepting it.
