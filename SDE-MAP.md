# Repository Semantic Map

This initial routing map was established by Conditor from accepted governing inputs. It does not infer application semantics.

## Governing inputs

- Canonical execution contract: `docs/requirements/REHEARSAL-ACCEPTANCE-LEDGER.md`
- Accepted requirement artifacts:
- `rehearsal-dependency-lock`: `docs/requirements/REHEARSAL-DEPENDENCY-LOCK.md`
- `rehearsal-external-data-boundary`: `docs/requirements/REHEARSAL-EXTERNAL-DATA-BOUNDARY.md`
- `causa-ui-experience-requirements`: `docs/requirements/CAUSA-UI-EXPERIENCE-REQUIREMENTS.md`
- `wasm-loading-experience-requirements`: `docs/requirements/WASM-LOADING-EXPERIENCE-REQUIREMENTS.md`
- `agent-decision-reduction-profile`: `docs/requirements/AGENT-DECISION-REDUCTION-PROFILE.md`
- `rehearsal-agent-interface-freeze`: `docs/requirements/REHEARSAL-AGENT-INTERFACE-FREEZE.md`
- `rehearsal-acceptance-ledger`: `docs/requirements/REHEARSAL-ACCEPTANCE-LEDGER.md`
- `rehearsal-interface-manifest-r001`: `docs/requirements/REHEARSAL-INTERFACE-MANIFEST-R001.md`
- `defect-class-closure-and-release-gates`: `docs/requirements/DEFECT-CLASS-CLOSURE-AND-RELEASE-GATES.md`
- `screen-contracts`: `docs/requirements/SCREEN-CONTRACTS.md`
- `state-transition-tables`: `docs/requirements/STATE-TRANSITION-TABLES.md`
- `command-event-catalog`: `docs/requirements/COMMAND-EVENT-CATALOG.md`
- `product-requirements`: `docs/requirements/PRODUCT-REQUIREMENTS.md`
- `causa-storage-release-v1-0-0`: `docs/requirements/CAUSA-STORAGE-RELEASE-v1.0.0.md`
- `security-threat-model`: `docs/requirements/SECURITY-THREAT-MODEL.md`
- `design-decision-boundaries`: `docs/requirements/DESIGN-DECISION-BOUNDARIES.md`
- `kickoff-execution-packet`: `docs/requirements/KICKOFF-EXECUTION-PACKET.md`
- `hackathon-boundaries`: `docs/requirements/HACKATHON-BOUNDARIES.md`

## Semantic areas

| Semantic area / feature | Purpose | Location | Manifest | Notes |
|---|---|---|---|---|
| Initial application mission | Establish the smallest semantic model required by the accepted contract | `src/engine/` | not established yet | Domain concepts, legal state, transitions, invariants, and guards are intentionally unknown until the governing inputs are interpreted. |

## Repository-wide composition

- Composition/root entry point: `src/kernel/bootstrap.ts`
- Shared contracts: see the governing inputs above.
- Architecture checks: installed Ordo/Praxis verification plus repository build/tests.
- Boundary checks: installed Limen and Aegis contracts where required.

## Areas without separate manifests

No feature manifest is generated merely to satisfy structure. Create one when the initial mission establishes a real semantic ownership boundary.
