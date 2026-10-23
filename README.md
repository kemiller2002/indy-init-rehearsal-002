# Causa — Rehearsal 002

Causa is an evidence-bounded root-cause investigator. Its F# domain engine runs in the browser as WebAssembly behind
the Limen boundary, with Forma (design system) and Folio (print) from the verified Echelon stack. **The application
contains no investigations**: you connect a GitHub repository you choose, and Causa reads, validates and replays its
Causa v1.0.0 records, and writes new ones with optimistic concurrency and an independent read-back.

## Quick start

```bash
make run        # checks the toolchain, restores, builds the engine and WASM site, serves http://127.0.0.1:8080/
make test       # conformance (frozen causa-data v1.0.0), engine flow and browser tests
make validate   # stack lock, vendored-source identity, zero demonstration data, kernel type-check
make package    # dist/causa-site.tar.gz
make deploy     # explains the GitHub Actions → GitHub Pages deployment
```

Requires .NET SDK 10.0.402 (`global.json`), Node.js 24 and Python 3; `make doctor` says exactly what is missing.
To use the app, open it, choose **Connect data repository**, and enter owner, repository, branch, workspace
(`fixtures` for the frozen read-only set, or a run id such as `team-2026-10`) and a short-lived, fine-grained token
limited to that repository. The token stays in memory only.

- Architecture: [docs/architecture/ARCHITECTURE.md](docs/architecture/ARCHITECTURE.md)
- Stack verification and lock: [evidence/stack/verification-report.md](evidence/stack/verification-report.md)
- Requirements (pinned from Indy-init): [docs/requirements/](docs/requirements/)

## Repository operating system

This repository is a greenfield pilot running Praxis 3.16.0, Echelon Foundry's
repository operating system. An older installation may also have `./ros`, a
compatibility alias of `./praxis`.

## Start here

1. Read [`AGENTS.md`](AGENTS.md) and [`BOOTSTRAP.md`](BOOTSTRAP.md).
2. Complete [`PROJECT-CHARTER.md`](PROJECT-CHARTER.md).
3. Establish the baseline in [`context/CURRENT-STATE.md`](context/CURRENT-STATE.md).
4. Select the first bounded mission and its observable acceptance criteria.
5. Record durable evidence, decisions, and handoffs as the work proceeds.

## Local operating commands

```bash
./praxis work begin --id TASK-001 --occurred-at TIMESTAMP --type task
./praxis work context TASK-001
./praxis status
./praxis registry check
./praxis registry build
./praxis validate
```

`work context` reports legal actions and completion evidence. Validation errors include repair instructions; use `./praxis validate --json` for machine-readable output. Complete work with explicit evidence paths as described in `docs/work-protocol.md`.

The installed snapshot is self-contained. It does not read from the source Praxis
repository. `.ros/installation.json` records the package version and checksums
of installed files.

## Pilot rule

The operating system is itself under evaluation. Do not infer that
Indy Init Rehearsal 002 is a validated discipline, method, or product merely because
the repository follows a rigorous process. Measure whether the process improves
decisions, traceability, handoffs, and rework relative to the declared baseline.
