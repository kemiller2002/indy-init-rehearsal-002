# Agent Decision Reduction Profile: Causa Rehearsal and Competition

Status: normative *implementation-profile defaults* for the next rehearsal. Not a permanent change to generic domain semantics. No new experiment is authorized by this document.

## Purpose
Minimize avoidable agent choices, integration churn, and repeated release candidates while preserving correctness and the reversible/experimental boundaries in DESIGN-DECISION-BOUNDARIES.md.

## ADRP-001: Source precedence and conflict stop
Use this order for the next rehearsal: (1) organizer rules and explicit safety/security invariants; (2) frozen causa-data v1.0.0 contract and locked domain invariants; (3) this implementation profile for choices explicitly covered here; (4) STATE-TRANSITION-TABLES, POLICY-ENGINE, COMMAND-EVENT-CATALOG, INVESTIGATION-SEMANTICS; (5) SCREEN-CONTRACTS and CAUSA-UI-EXPERIENCE-REQUIREMENTS; (6) BUILD-DAY-EXECUTION, IMPLEMENTATION-SLICES, and other planning documents; (7) long-term PRODUCT-REQUIREMENTS for non-conflicting extensions. This order does not waive mandatory requirements. If two mandatory statements cannot both hold, record the exact citations and STOP that slice for coordinator resolution rather than silently selecting one.

## ADRP-002: Scope and screen inventory
The 25 screen contracts are a long-term semantic catalog, NOT a mandate for 25 independently implemented routes during a timed build. Reuse a single workspace shell with views for overview, hypotheses, evidence, timeline, causes/verification, report, and settings/diagnostics; use detail panels for entity inspection. Every implemented screen MUST satisfy its semantic contract. Preserve unimplemented P1/experimental screens as explicit scoped work, not false passes. P0 requirements in CAUSA-UI-EXPERIENCE-REQUIREMENTS.md remain required for integrated acceptance.

## ADRP-003: Locked UI composition default
Default shell: responsive top application bar with identity/sync indicator; desktop/iPad navigation rail or tabs; investigation header showing lifecycle, blockers, and next legal action; main content region; contextual detail panel; mobile stacked layout. Overview presents observation, active hypotheses, contradictions, obligations, and next action before secondary history. Prefer Forma components; do not introduce an independent design system. CSS handles layout and loading indicator; no new JS layout framework. Accessibility and responsive obligations override pixel choices.

## ADRP-004: Domain/UI contract
UI receives a versioned projection and legal capabilities from the F# engine. Commands include expected aggregate version and return accepted event(s) or typed faults. The UI never computes legality, evidence weight, confidence, or causal truth. A successful command refreshes all dependent views from the same projection version. Invalid or unsupported required semantics are read-only/blocked. Historical and frozen-fixture views are read-only.

## ADRP-005: Data and persistence
Use frozen causa-data/contract/v1.0.0/CONTRACT.md and tag causa-storage-v1.0.0 as the sole wire contract for this profile. Seed fixtures are immutable; working copies use separate paths. Storage adapters implement the existing provider-neutral interface and optimistic concurrency. No agent may improvise alternative field names, ordering, event shapes, or snapshot semantics. Show distinct local and remote persistence status; never claim live GitHub writes from mocked tests. Changes to frozen contract require explicit versioning and authorization.

## ADRP-006: Reuse and dependency decision
Use existing F#/.NET, Limen, Forma, Folio, Aegis, Praxis, Ordo, Dokimos, and Percepta capabilities when they meet the contract. Before EVERY run, discover the newest approved stable releases across all in-scope Echelon systems, resolve and test their compatibility as a set, then pin exact verified versions in the run lock. Historical R001 versions are not defaults. No automatic downgrade; block and resolve incompatibilities or obtain an explicit time-bounded exception. Follow REHEARSAL-DEPENDENCY-LOCK.md. Do not add new UI, state-management, graph, or animation dependencies without an explicit integration-cost justification and coordinator approval. Where a library lacks required functionality, implement the smallest isolated adapter.

## ADRP-007: Deterministic graph and timeline
Render graph edges only from canonical projected relationships; deterministic layout seeded by stable IDs, with accessible textual alternative. Timeline sorts by canonical sequence/version, not wall-clock alone. Entity selection opens the same inspector from graph, timeline, search, and comparison. Do not implement independent causal inference, synthetic confidence, or implicit event mutation.

## ADRP-008: Agent ownership and interfaces
Assign one owner each for F# domain/policy, UI/WASM, storage/auth adapter, and integration/release evidence. The coordinator owns cross-boundary decisions and source-of-truth updates. Freeze command/projection/provider interfaces before parallel fan-out. Agents may work in parallel on separate files/slices but MUST NOT independently change shared contract files. Contract changes require a single owner, explicit version, consumer notification, and targeted compatibility proof.

## ADRP-009: Defect-class prevention and review
Before fan-out, list critical invariants and known R001 defect classes, especially forged-history validation, policy bypass, persistence truth, and deployed-SHA acceptance. Run an independent architecture/invariant review before the first release candidate. For critical/recurring defects, close the class rather than only the example, with original reproduction, adversarial variants, invariant enforcement, independent approval, and SHA-bound proof. Dokimos verified completion must remain blocked until evidence exists.

## ADRP-010: Test cadence and release batching
F# compile/unit and targeted invariant checks run during implementation. Integration/browser/accessibility tests may be batched to reduce queue overhead but cannot be suppressed when needed to validate a risky boundary. First full candidate runs the complete applicable matrix. Avoid repeated deployments for individual fixes; batch related changes. A release is Accepted only when the deployed exact SHA has a successful deployed acceptance job. Never claim elapsed-time savings until measured.

## ADRP-011: No-decision defaults for common UI states
No data: explicit empty-state action; loading: HTML/CSS shell before WASM; failure: visible recoverable error; offline: preserve local changes and label unsynced; version conflict: no overwrite, preserve edits and show reconciliation; unauthorized action: disabled/explained by F# capability; historical mode: visibly read-only; unknown semantics: preserve and block unsafe writes; reduced motion: disable nonessential animation. No invented progress percentage or confidence score.

## ADRP-012: Build order and cut strategy
Order: freeze contract and interface pins; F# kernel and invariants; frozen fixture replay; shell/loading/navigation; end-to-end legal action; local persistence truth; GitHub adapter; timeline/evidence/hypotheses; causal graph/comparison/report; proof and deployed acceptance. If time constrained, cut experimental/advanced features according to KICKOFF-EXECUTION-PACKET.md, but do not silently cut P0 scope or essential invariants. Any P0 incompletion must be reported as FAIL/BLOCKED, never PASS.

## ADRP-013: Decision escalation
Agents may decide local naming, refactoring, and visual details within this profile. Escalate: new dependency; schema or event changes; domain policy semantics; auth/security shortcuts; breaking interface; cutting P0; new release candidate after a failed gate; changing frozen fixtures. Escalations require options, impact, recommendation, and recorded decision. Do not poll the coordinator for reversible local details.

## ADRP-014: Single completion ledger
For each requirement, record ID -> owning slice -> code path -> test/proof ID -> verified SHA -> outcome (PASS/FAIL/BLOCKED/NOT RUN). Include a separate class-closure ledger and deployed SHA. Work is not complete because a UI renders or a local CI run is green. No invented evidence.

## ADRP-015: Measurement
Capture decision escalations, interface changes after fan-out, repeated defect classes, number of release candidates, reviewer findings, first green build, final accepted elapsed time, CI queue/test duration, token consumption, and correctness coverage. Compare with R001, acknowledging non-equivalent scope. A 30–60-minute saving remains a hypothesis, not a guarantee.

## ADRP-016: Competition compliance
Preserve blank-application kickoff rules and provenance. Prepared requirements, library capabilities, design guidance, frozen fixtures, and scripts are subject to organizer approval; do not transfer a prebuilt competition app where prohibited.

## Still deliberately open
- Universal causal DAG, confidence/calibration formulas, EDF-specific experimental semantics, automated causal inference, third-party plugin marketplace, enterprise RBAC, and future storage providers remain experimental/deferred.
- Exact production OAuth/Fides integration, provider deployment topology, and physical local browser durability require capability/security checks before selecting a concrete adapter.
- Pixel-level appearance and component composition inside the locked hierarchy remain reversible.
These are not invitations for each agent to invent a new architecture during the rehearsal.

## Pre-experiment readiness checklist
- [ ] Discover latest approved stable versions, resolve the complete Echelon stack, and verify compatibility before locking for this run.
- [ ] Confirm canonical Praxis input folder/grammar in every affected repo and create work items there.
- [ ] Freeze command/projection/provider interfaces and record owners.
- [ ] Validate frozen fixture digests and five-scenario golden replay.
- [ ] Resolve any mandatory-spec contradictions discovered in preflight.
- [ ] Produce one executable acceptance ledger and defect-class proof plan.
- [ ] Obtain organizer rule confirmation for pre-created data and artifacts where necessary.

## Supporting normative documents
DESIGN-DECISION-BOUNDARIES.md; STATE-TRANSITION-TABLES.md; SCREEN-CONTRACTS.md; CAUSA-UI-EXPERIENCE-REQUIREMENTS.md; WASM-LOADING-EXPERIENCE-REQUIREMENTS.md; DEFECT-CLASS-CLOSURE-AND-RELEASE-GATES.md; CAUSA-STORAGE-RELEASE-v1.0.0.md; KICKOFF-EXECUTION-PACKET.md.
