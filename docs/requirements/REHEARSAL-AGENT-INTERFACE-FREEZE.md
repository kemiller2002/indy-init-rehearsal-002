# Next Rehearsal Agent Handoff Contract

Status: normative boundary and acceptance contract. The actual source-level F# signatures MUST be extracted and pinned before fan-out; these are not fabricated compile-ready interfaces.

## Ownership
- Domain owner: F# command/decision/event semantics, policy and capability derivation.
- UI owner: Forma shell, Limen host, screen projections, accessibility and loading experience.
- Persistence owner: provider-neutral adapter, frozen contract mapping, local queue, optimistic concurrency and remote acknowledgment.
- Integration owner: dependency lock, contract tests, CI, review, deployed-SHA proof, release-candidate batching.
- Coordinator: interface change control, escalations and requirements ledger.

## Boundary A: command submission
Input MUST include investigation ID, actor/provenance, typed command, expected aggregate version, and command identity/idempotency context where supported. Output MUST distinguish accepted semantic events/new version from typed rejection/fault. UI MUST NOT generate authoritative events directly. Domain owner publishes concrete F# signatures and JSON/WASM boundary shape from source.

## Boundary B: versioned projection
A single immutable projection identifies investigation, aggregate version, lifecycle, hypotheses, evidence, contradictions, obligations, causal assertions, permitted capabilities and reasons for blocked actions. Fields MUST map to existing domain types; do not add synthetic confidence, inferred cause, or made-up severity/owner. Every screen must consume the same versioned projection or explicitly label stale state.

## Boundary C: replay
Given an immutable canonical ordered history and semantic/policy version, replay validates every event through the authoritative engine and either yields a projection or a typed invalid-history fault. No alternative browser-only validator. Frozen fixtures are read-only.

## Boundary D: persistence
Provider-neutral operations: load, append with expected version, acknowledge remote persistence, surface conflict, and recover local edits. Separate local-durable from remotely-synced status. Storage adapter alone maps to causa-data v1.0.0 paths/serialization. No secrets in browser bundles.

## Boundary E: report
Folio receives a read-only, versioned report projection with provenance, unresolved uncertainties, conclusions and verification status as present. Report rendering cannot elevate an unverified hypothesis to a confirmed cause.

## Contract freeze procedure
1. Extract actual types and exported interfaces from accepted R001 F# source and Limen adapter.
2. Publish one versioned interface manifest including module/type names, request/response examples, error variants and contract tests.
3. Run golden replay of all five frozen scenarios and negative-history cases.
4. Each owner signs off against the exact interface-manifest SHA before parallel work.
5. Breaking change requires coordinator decision, manifest revision, consumer notification and compatibility proof. No agent edits another owner's shared interface independently.
6. An interface not extracted and validated is BLOCKED, not frozen.

## Mandatory negative cases
Stale aggregate version; forged/illegal history; unknown required semantic extension; unauthorized command; duplicate submission; conflicting remote write; offline queue; mismatched frozen schema; incomplete verification; incorrect deployed SHA.

## Completion evidence
Interface manifest SHA, owner matrix, producer/consumer tests, negative-case results, and decision log. Integrate defect-class closure from DEFECT-CLASS-CLOSURE-AND-RELEASE-GATES.md.
