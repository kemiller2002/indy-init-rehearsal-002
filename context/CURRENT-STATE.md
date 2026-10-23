# Indy Init Rehearsal 002 current state

## Repository status

Newly initialized with Praxis 3.16.0.

## Observed facts

- No domain evidence has been accepted.
- No vertical slice has been selected.
- No discipline-boundary claim has been tested.

## Assumptions

- A small, concrete communication problem can exercise the operating model.

## Active work

Complete the charter and select the first bounded pilot slice.

## Largest decision-relevant unknown

Which first use case will provide measurable value while exposing the important
communication constraints?

## Baseline

Not yet recorded. Define how the same slice would be approached without Praxis and
which comparison measures are feasible.

<!-- conditor:ordo-baseline:start -->
# Current State — Conditor Baseline

This is the initial greenfield baseline. It records what Conditor can prove before application implementation begins.

## Established facts

- The declared Echelon capabilities were planned and installed through their supported lifecycle contracts.
- Accepted requirement artifacts were materialized from immutable sources declared by `conditor.json`.
- Canonical execution contract: `docs/requirements/REHEARSAL-ACCEPTANCE-LEDGER.md`.
- The generated `src/engine/Domain.fs` state is a scaffold placeholder, not a claim that the application domain has been modeled.

## Accepted requirement artifacts

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

## Unknowns

- Application domain concepts have not yet been derived.
- Important legal state and illegal states have not yet been identified.
- Legal transitions, invariants, guards, capabilities, and effect obligations have not yet been established.
- Semantic feature boundaries and ownership are not yet known.

## Obligations before implementation expands

1. Read the canonical execution contract and its normative references.
2. Identify the smallest domain concepts and legal state required by the first meaningful vertical behavior.
3. Establish legal transitions, invariants, guards, capabilities, and explicit effects required by that behavior.
4. Update `SDE-MAP.md` and create feature manifests only when real semantic ownership is known.
5. Preserve unknowns explicitly rather than converting missing knowledge into assumptions.

## Next action

Execute the initial Praxis mission using the accepted governing inputs and establish the first evidence-backed Ordo semantic slice.
<!-- conditor:ordo-baseline:end -->
