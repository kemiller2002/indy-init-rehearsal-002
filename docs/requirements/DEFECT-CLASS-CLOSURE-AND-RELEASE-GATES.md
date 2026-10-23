# Defect-Class Closure and Release-Candidate Efficiency Requirements

Status: normative process requirements for future Causa rehearsal and competition runs. This document does not retroactively change R001 evidence or authorize a new experiment.

## Objective
Reduce repeated release-candidate cycles, recurring defect classes, elapsed time, and token consumption without reducing correctness or acceptance coverage.

## DCC-001: Early invariant inventory
Before implementation fan-out, Ordo-guided planning MUST identify critical invariants, authority boundaries, single decision paths, storage contracts, and explicit failure modes. Record the owner and proof obligation for each. F# domain semantics remain authoritative; process systems MUST NOT duplicate policy logic.

## DCC-002: Early adversarial review
A bounded independent reviewer MUST inspect architectural decisions, interfaces, and critical invariants before the first release candidate, targeting completion by T+90 to T+120 in a seven-hour run. Review MUST NOT wait for browser or deployment acceptance. Review findings must be actionable, reproducible, and assigned to a requirement/invariant.

## DCC-003: Defect-class classification
Every critical finding and every recurring major finding MUST record: defect-class ID, minimal reproduction, affected requirements, underlying architectural cause, invariant violated, impacted paths, severity, proposed class-level prevention, owner, and closure evidence. Ordinary isolated defects may use lighter records. Multiple manifestations of one cause MUST be linked to the same class.

## DCC-004: Class-level correction
A critical/recurring defect MUST NOT be closed solely because its reported example stops failing. The fix MUST prevent the broader class wherever feasible (e.g. replay all event histories through the canonical Decide/Evolve validation path, rather than patching specific forged sequences). If structural prevention is impossible, document why, enumerate remaining exposure, and require an explicit risk decision.

## DCC-005: Proof of closure
Critical/recurring defect-class closure MUST include (a) targeted regression reproducing the original failure, (b) additional adversarial or mutation cases spanning the class, (c) evidence of invariant enforcement at the authority boundary, and (d) independent review of the correction. Evidence MUST be tied to the exact source SHA. Reappearance of the class reopens it and blocks verified completion.

## DCC-006: Praxis and Dokimos binding
Praxis MUST track defect-class IDs, affected requirements, status, and proof locations in its documented work-item artifacts. Dokimos verified completion MUST fail when an in-scope critical class is open, reopened, or lacks SHA-bound closure evidence. Do not use GitHub Issues as the requirements/work-item intake; use the documented Praxis input directory. Where current tooling cannot enforce this, record the missing control as BLOCKED and implement the smallest viable enforcement gate.

## DCC-007: Release candidate policy
Avoid deploying a release candidate for every patch. Batch compatible fixes behind a single integration gate; run targeted F# tests and compilation during implementation, then a full integrated test/acceptance gate for the candidate. No fixed time delay may prevent a safety-critical or high-risk targeted check. Do not defer detection of critical architecture defects. Additional candidates require a documented failed gate, material change, or release rationale.

## DCC-008: Acceptance identity
A release MUST NOT be called Accepted until the deployed artifact identifies the exact CI-verified SHA and the deployment acceptance job for that SHA concludes successfully. A green local or mocked browser run is insufficient. Failed, pending, skipped, or unread acceptance jobs cannot be treated as PASS.

## DCC-009: Preserve test rigor
No tests are deleted, weakened, skipped, or reclassified to improve elapsed time. F# domain tests, cross-boundary integration tests, browser/accessibility tests, and deployed acceptance remain required at their respective gates. Mocked GitHub persistence cannot be represented as verified live writes.

## DCC-010: Measured efficiency
Per run, Percepta/research evidence MUST capture: first compiled/green milestone; time to first candidate; number of candidates; CI/test runtime and queue/wait time; reviewer findings by class/severity; recurrence count; time and tokens spent on class fixes; accepted release time; defect escape rate; and accepted requirement coverage. Distinguish observed savings from hypotheses and report both correctness and efficiency.

## DCC-011: Baseline and target
Use R001 as historical baseline: first green CI T+88.3 min, accepted release T+261.8 min, four adversarial review rounds and recurring forged-history failures. A 30–60 minute overall reduction is a *hypothesis to evaluate*, not an established saving or acceptance criterion. Compare equivalent scope and rigor; do not claim causal improvement from a single uncontrolled run.

## DCC-012: Responsibility boundaries
Ordo defines decision/invariant boundaries; Praxis manages requirement and defect-class state; Dokimos enforces verified evidence; Percepta measures outcomes; the F# engine enforces domain policy. Preserve existing contracts and minimize framework changes.

## Acceptance matrix
- Every critical invariant has an owner and proof obligation before implementation fan-out.
- An independent architecture/invariant review occurs before the first release candidate.
- No critical or recurring defect class is closed by instance-only patching.
- Every such closure has SHA-bound regression, adversarial coverage, and independent approval.
- Verified completion is blocked on unresolved critical classes.
- The exact deployed SHA has a successful acceptance-job conclusion before Accepted is asserted.
- Final report includes release count, time breakdown, recurrence rate, token cost, and correctness/coverage evidence.

## Implementation intake
Translate these requirements into Praxis input artifacts using the exact documented folder and grammar in each target repository. Do not guess the path or create GitHub Issues. Do not start another experiment merely by adding these requirements.
