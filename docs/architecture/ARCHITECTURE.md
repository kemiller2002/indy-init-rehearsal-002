# Causa Rehearsal 002 — architecture

## One sentence
A pure F# engine, compiled to .NET WebAssembly and hosted by the Limen 0.9.0 browser kernel, owns every decision;
investigation data lives only in an external GitHub repository the user explicitly selects; the browser renders
one flat projection with Forma 0.9.0 (Causa brand) and Folio 0.4.0.

## Layers and ownership

| Layer | Path | Owns | Must not |
|---|---|---|---|
| Domain (vendored, pinned) | `src/domain` | commands, decisions, events, transitions, history admissibility (`Decide`, `Evolve`, `History.validate`) | be edited (byte-identical to indy-init-rehearsal-001@55f49f61; `tools/vendor/vendor.py verify`) |
| Storage contract (vendored, pinned) | `src/contract` | Causa v1.0.0 wire format, 17-stage reader, writer, schemas (embedded) | be edited (causa-data@causa-storage-v1.0.0) |
| Application engine | `src/engine` | session state, GitHub provider *as data*, command forms, capabilities, projection, Limen boundary (`Shell.dispatch`) | perform I/O, read a clock or draw entropy (both arrive per message from the host) |
| WASM host | `src/wasm/Program.cs` | one `[JSExport]`, the single state cell, entropy/time supply | decide anything |
| Kernel / page | `src/kernel` | static loading shell, Limen bindings, Forma/Causa CSS, focus and print mechanics | compute legality, truth, confidence or counts |

## Boundaries (REHEARSAL-AGENT-INTERFACE-FREEZE)
- **A — command submission:** `CommandForms.build` (input shape only) → `CommandEnvelope { CommandId, Investigation,
  ExpectedVersion, Actor, RequestedAt (ms-truncated UTC) }` → `Decide.execute` → events or typed `DomainError`s.
  The browser never constructs commands or events; it sends semantic events (`cmd.*`).
- **B — projection:** `View.project` builds the complete Limen `ViewState` from one aggregate version; capabilities
  come from `Decide.probe` and the domain's `Transitions` tables (`Capabilities.judge`).
- **C — replay:** every read goes through `Store.validate` (path, encoding, canonical JSON, schemas, hashes,
  sequence, event ids, correlation, `History.validate`, snapshot). Invalid data fails closed and is never shown.
- **D — persistence:** `GitHub.fs` describes REST requests as data; `Shell.fs` sequences them as Limen Http effects:
  blobs → tree (base = the decision's base tree) → commit (parent = the decision's base commit) → non-forced ref
  update → independent read-back (ref, commit, tree, byte-equal blobs, re-validation). Non-fast-forward → reload and
  rebase only if this investigation is unchanged, else conflict (pending change preserved). Unknown outcome →
  reconcile by ancestry (compare API). Success is reported only after read-back.
- **E — report:** the Report tab renders the same projection inside Folio `ef-print-*` elements; it never upgrades
  a hypothesis to a cause.

## External data
- Fresh launch: zero investigations; "Connect data repository" requires owner, repository, branch, workspace
  (`fixtures` = frozen read-only v1.0.0 set, or a run id → `workspaces/demo/<run>/`), and a token.
- The token is engine memory only: sent solely in the `Authorization` header of `api.github.com` requests (Limen
  never surfaces request headers in diagnostics), never projected, stored, logged or put in a URL. Reload forgets it.
- Writes touch only `workspaces/demo/<run>/investigations/<id>/{manifest.json,events.ndjson,snapshot.json}`.

## Defect classes closed
- **DC-001** stale-base publication (found by the independent test owner): see `docs/decisions/DC-001.md`.

## Known limitations (recorded, not hidden)
- The WASM bundle is untrimmed (~30 MB uncompressed site): the vendored domain uses reflection-based formatting.
- Pending changes are held in tab memory, not in durable local storage; the UI says so ("held in this tab only").
- UI-002 (graph canvas) is delivered as an accessible textual relationship list only; UI-007/008/012 (P1) are not
  implemented.
- Proposal acceptance (AcceptProposal) is not offered in the UI (it needs a created target entity in one command).
