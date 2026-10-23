# Independent closure review 5: commit 1670dcb (review-4 findings DC-005 Q1–Q4, DC-006 B1)

- **Reviewer:** an independent agent. It changed nothing under `src/` or `tests/` and committed or pushed nothing.
  This file is the only file it wrote in the repository.
- **Date:** 2026-10-23. **Working tree:** HEAD `1670dcb`, clean (`git status --short` empty, before and after).
- **Scope:** `git diff 6379358 1670dcb -- src tests`:
  - `src/engine/Session.fs` (new `OpRefreshReread`);
  - `src/engine/Shell.fs` (`onRefresh`, `onEffect` routing);
  - `src/kernel/main.js` (fetch wrapper that counts downloads);
  - tests 24, 24b, 24c, 24d and the new boot spec case.

  I also read `onWrite` (verify and save paths), `refresh`, `GitHub.classify` and `GitHub.compare`, the
  `Reapply` paths, and the loader's `fetch_like` in `dist/site/wasm/_framework/dotnet.js`.

## What was run

1. `dotnet test tests/App.Engine.Tests -c Release` (after sourcing `env.sh`) → **Passed 57, Failed 0**.
2. `npm run -s check` → exit 0, with no output.
3. `npx playwright test tests/browser/boot.spec.js` → **7 passed** (41.2 s). This includes the new "retried download
   on a slow connection" case (15.7 s).
   - `dist/site/main.js` is byte-identical to `src/kernel/main.js` (checked with `cmp`).
   - `dist/site/build-info.json` names `sourceCommit 6379358`. 1670dcb changes only `main.js` and the engine, and
     `main.js` is identical, so the browser results are valid for item 2.
   - The engine items were judged from the .NET tests and probes, not from the browser.
4. **Engine probes P1–P4.**
   - These were xunit facts in a throwaway copy of the test project at `…/scratchpad/probe6/fs`, outside the
     repository.
   - The copy had the same `Fixtures.fs`, `FakeGitHub.fs`, `Harness.fs` and `EngineTests.fs`. Only the
     ProjectReference and the data path were made absolute, and the probes were appended at the end.
   - Each probe prints the state it observes.
5. **Browser probes.**
   - These ran in Playwright with `/opt/pw-browsers/chromium-1194` against `dist/site`, served by
     `scripts/serve.mjs` on port 4397.
   - A small Node proxy sat in front of it on ports 4392–4396. It could answer 503 for
     `System.ComponentModel.EventBasedAsync.*.wasm`, and it could send `dotnet.native.*.wasm`'s body in 30 chunks
     over 15 s.
   - One comparison served a copy of `dist/site` with `main.js` from `6379358`.
6. All probe processes were killed and `…/scratchpad/probe6` was deleted.

## Summary

| Item | Verdict | Worst remaining variant |
| --- | --- | --- |
| 1. DC-005 reload | **OPEN** | **Medium** (R1, a regression in this commit): after another writer's commits have been shown, a stale ref read rolls the view back past them, silently. **Low–medium** (R2): any compare failure classified `Rejected`, including 5xx, adopts the stale ref. **Low–medium** (R3): two back-to-back stale reads roll the verified save back with a "reset by someone" notice (no data lost). **Low** (R4): a reset followed by a second read that names the removed commit re-publishes it. |
| 2. DC-006 grace and downloads | **CLOSED** | Low, and not introduced by this commit: a persistent download failure that fails fast ends with the wrong message ("version mismatch"). Low: fetches that are not downloads are also counted. |

Review-4 Q1, Q2b, Q3 and Q4 are each fixed **for the scenario the review stated**. Tests 24 to 24d pass. Item 1 stays
OPEN because the new anchor creates R1.

---

## 1. DC-005 reload: OPEN

### What is correct

- **Termination (no stuck reload, no loop).** Every `onRefresh` branch either emits exactly one next request or clears
  `Refreshing` (`src/engine/Shell.fs:301-354`). The new branches behave as follows:
  - `OpRefreshCompare` with `behind` emits one `OpRefreshReread` (`:318`).
  - `OpRefreshReread` emits only `adopt` (OpCommit → OpTree, which clears `Refreshing`) or `stop` (`:321-328`).
  - No branch re-emits `OpRef` or `OpRefreshCompare`, so the chain is bounded at OpRef → Compare → Reread → Commit →
    Tree.
  - Probe P4 checked three failure cases: a network error on the re-read, a compare answered 500, and a compare
    answered 422. Each ended with `refreshing=false`.
- **Routing.** `OpRefreshReread _` is routed to `onRefresh` only for `generation = 0 && live.Refreshing`
  (`Shell.fs:538`), the same guard as the other reload ops. A write's answers carry generation ≠ 0 and cannot reach
  it.
- **Re-apply.** `Reapply` is handled on every outcome:
  - `stop` holds the change visibly (`:296-297`);
  - every adopt goes through `OpTree`, which handles `Reapply` (`:339-352`);
  - the old `behind` keep-branch that held the change no longer exists, so no path drops `Reapply`.
- **Review-4 cases.**
  - Q1 and Q2b (one stale read after a moved or rebased save) are now kept, because the anchor is `LastVerified`
    (`:307`). Tests 24 and 24b pass.
  - Q3 (a deliberate reset) is adopted after one confirming re-read, and the next save builds on it. Test 24c passes.
  - Q4 (a compare answered 404 or 422) adopts the ref (`:303`). Test 24d passes.
- **Remote data.** No probe lost remote data.
  - Every PATCH is non-forced (`GitHub.updateRef`, `"force":false`).
  - A save parented on a shown head that is too old is refused as not fast-forward and rebased.
  - In P1 and P2 the next save's parent was the real branch head.

### R1 (medium, a regression in 1670dcb): an anchor older than the shown head lets a stale read roll the view back

The anchor is `LastVerified` whenever one exists (`Shell.fs:307`). `LastVerified` is set only by this session's own
saves (`:519`). It is not advanced when a reload adopts a newer head.

So once this session has saved once, and a later reload has shown other writers' commits (Head = H2, a descendant of
LastVerified = c1), the guard protects only c1, not the shown H2:

- a stale read naming an intermediate H1 gives `compare c1...H1` = `ahead`, and H1 is adopted;
- a stale read naming c1 itself equals the anchor and is adopted directly, with no compare (`:310`).

Either way the view moves back in history with no notice. At `6379358` the anchor was `live.Head.Commit`, so both
cases were kept (`behind`). This is the steady-state case: several saves, with other writers moving the branch in
between.

**Repro P1** (helpers from `EngineTests.fs`; `refBody` as in test 24):

```fsharp
let saved = connected "p1" (workspaceRepo "p1") |> createInvestigation "Mine" "x"   // c1 = saved.Repo.Head
let r1 = saved.Repo |> otherWriterAppends prefix openIds[0] "o1"                   // h1
let r2 = r1 |> otherWriterAppends prefix openIds[1] "o2"                           // h2
let shown = { saved with Repo = r2 } |> fire "refresh"                             // head = h2
shown |> withRule isGetRef (Respond(200L, refBody h1)) |> fire "refresh"           // (a)
shown |> withRule isGetRef (Respond(200L, refBody c1)) |> fire "refresh"           // (b)
```

Observed:

| Step | State |
| --- | --- |
| After the first reload | `head=2a50161 (=h2) lastVerified=a26c6c1 (=c1)` |
| (a) | `refreshing=false head=44fe311 (=h1) repoHead=2a50161`. The notice is unchanged (no warning). |
| (b) | `head=a26c6c1 (=c1) repoHead=2a50161` |

A save after (a) was parented on h2, through the not-fast-forward rebase. Nothing was lost.

**Fix direction.** Keep one "newest known commit" field.

- Set it to the verified commit on every save.
- Set it to the adopted commit whenever a reload adopts a commit that compares `ahead` of it, or that equals it.
- Use that field as the anchor.

Equivalently, set `LastVerified`'s sha, or a separate field, to `head.Commit` in the `OpTree` adopt when the adopted
commit came from an `ahead` compare. Add P1 (a) and (b) as tests.

### R2 (low–medium): a 5xx (or 400 or 409) on the compare adopts the stale ref

`OpRefreshCompare commit, Error(NotFound | Rejected _) -> adopt commit` (`Shell.fs:303`). `GitHub.classify` maps
**every** status not otherwise handled to `Rejected(s, …)`. That includes 409 (`GitHub.fs:217`) and 400, 500, 502,
503 (`:219`). So a transient server error on the compare is treated as "no common history".

**Repro (P4 b):** after a consistent save (head = c),
`saved |> withRule isGetRef (Respond(200L, refBody h0)) |> withRule isCompare (Respond(500L, "{}")) |> fire "refresh"`.

Observed: `refreshing=false head=de74cd0 (=h0) repoHead=372734f`. The verified save is gone from the view, and no
failure is shown.

**Fix:** match only `NotFound` and `Rejected(422L, _)`, and send other statuses to `stop`.

### R3 (low–medium): two back-to-back stale reads roll back a just-verified save, with a misleading "reset" notice

The re-read is sent **immediately** after the compare answer. Replica lag of seconds, which is what motivated
DC-005, can answer both reads stale. The engine then adopts the older commit as a "reset".

**Repro P2:** test 24 with a third rule `withRule isGetRef (Respond(200L, refBody h0))` added.

Observed:

- The state is `head=ecc5fe0 (=h0) repoHead=5b9dd59 catalog=5`. The saved investigation is missing from the view.
- The notice reads "The branch on GitHub is older than the version shown before (it may have been reset by
  someone)…".

Neither loss nor re-publication occurs: the next save gave parents `[5b9dd59]`, which is our verified commit, after
the not-fast-forward rebase. That save had `catalog=7`, and `patchBodiesNeverForce` held.

**Fix direction:** space the re-read by a short delay, using a timer effect if the kernel offers one, or re-read up to
N times with back-off before concluding it was a reset.

### R4 (low): a reset, then a second read that names the removed commit, re-publishes it

`Ok again -> adopt again` (`:328`) adopts any second answer without a compare, including the newer pre-reset commit
served by a replica that has not yet seen the reset.

**Repro P3:**

1. `saved = … |> createInvestigation "First" "x"`, with `c1 = saved.Repo.Head`.
2. Reset the fake to h0, then add `withRule isGetRef (Respond(200L, refBody h0)) |> withRule isGetRef (Respond(200L,
   refBody c1)) |> fire "refresh"`.
3. Then `createInvestigation "Second" "y"`.

Observed:

- The view is `head=f6c1172 (=c1)`, and no notice is shown.
- The second save's parents are `[c1]`. The removed commit is back on the branch.

This needs a reset and a non-monotonic pair of replica answers within milliseconds. It is the same exposure the
pre-63b2232 code had for a single read, so it is low.

**Mitigation:** when the second read differs from `first`, compare it against `first` and the anchor before adopting
it. At minimum, do not adopt the anchor or one of its descendants silently after a `behind`.

---

## 2. DC-006 grace and downloads: CLOSED

**Evidence:**

- **Restored on every exit path.** The wrapper is installed at `src/kernel/main.js:103-112`, outside the `try`. The
  only `await` is inside the `try`. The `finally` restores `globalThis.fetch = browserFetch` (`:141`) on all four
  exits: success, a `kernel.start()` rejection, a timeout, and a loader failure. The version-mismatch check (`:143`)
  runs after the `finally`.
- **The counter.** Each call increments once and decrements once in `.finally`. A `.finally` keeps the original
  rejection, so the loader still sees the same failure.
  - A fetch that never settles keeps the counter above 0. That only re-arms the grace timer.
  - The 90 s `timeout` promise is independent (`:94`) and still ends a hung start. I checked this by reading the
    code; I did not run it.
  - After the `finally`, any late decrements are harmless.
- **Fetch captured before wrapping.** This does not apply to this loader. `dotnet.js` is loaded by `import()` inside
  `kernel.start()`, after the wrapper is installed. Its `fetch_like` reads `globalThis.fetch` at each call
  (`return globalThis.fetch(e, t||{credentials:"same-origin"})` in `dist/site/wasm/_framework/dotnet.js`). A loader
  that cached `fetch` earlier would leave the counter at 0, and the behaviour would fall back to review-4 behaviour.
  The new boot case would then fail, so the test guards this.
- **Body download is covered.** The loader passes SRI `integrity` on every asset. Chrome resolves such a fetch only
  after the whole body is verified.
  - Probe: the proxy sent headers immediately and trickled `dotnet.native.*.wasm` over about 15 s. An init-script
    trace recorded that fetch as starting at 65 ms and resolving at 15 104 ms.
  - With a 503 on `EventBasedAsync` on the first hit, the boot reached `ready` at 15 562 ms (`hits=2`). This is the
    real slow-body form of review-4 B1, not only Playwright's delayed route.
  - The repository's own case (a 15 s route delay) passes in 15.7 s.
- **A persistent failure still fails promptly.** The repository case "a 503 on one framework .wasm" passes in 12.6 s.
- **Types.** `npm run -s check` exits 0. The JSDoc on the rest parameter is accepted under `checkJs`.

### Remaining variants

- **Low, already at 6379358 and not introduced by this commit: wrong message for a fast persistent failure.** When a
  real server (not `page.route`) answers 503 on every attempt, `kernel.start()` *resolves* after the loader's
  terminal failure, in about 0.6 s. The page then shows "The application engine did not accept this page (version
  mismatch)" instead of the download message.
  - Repro: a proxy answering every `System.ComponentModel.EventBasedAsync.*.wasm` with `503 cache-control: no-store`.
  - Observed: `boot=failed ms=581 msg=version mismatch` with 1670dcb's `main.js`, and `ms=601`, the same message,
    with 6379358's `main.js`.
  - The failure is still prompt and recoverable; only the wording is wrong.
  - Fix: remember that a rejection was seen, and choose the download message when `kernel.status !== "running"`
    after one.
- **Low: the counter is not specific to downloads.** It counts every `fetch` made during start, not only `_framework`
  downloads. A slow non-loader request could hold off the loader failure, up to the 90 s timeout. If SRI were ever
  disabled, a body still streaming after its headers arrived would not be counted.
- **Low, carried over from reviews 3 and 4, not addressed:** after a `"loader"` failure, `kernel.start()` keeps
  running behind the failure screen.

## Recommended actions

1. **DC-005 (medium, R1):** advance the anchor when a reload adopts a commit that is ahead of it, and add P1 (a) and
   (b) as tests.
2. **DC-005 (low–medium, R2):** narrow `Shell.fs:303` to `NotFound | Rejected(422L, _)`.
3. **DC-005 (low–medium and low, R3 and R4):** delay or repeat the re-read before concluding it was a reset, and do
   not adopt a second answer that differs from the first without an ancestry check.
4. **DC-006 (low):** fix the failure message when `kernel.start()` resolves after a download rejection.
