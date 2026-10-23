namespace IndyInitRootCauseInvestigator.Engine

open System

// Lifecycle states: STATE-TRANSITION-TABLES.md §1–§12 (persisted states only;
// derived postures such as "Leading" are projections, never stored).

type InvestigationLifecycle =
    | Draft
    | Active
    | AwaitingEvidence
    | AwaitingVerification
    | Resolved
    | Closed
    | Archived

type HypothesisState =
    | Proposed
    | Plausible
    | Supported
    | Weakened
    | Falsified
    | Superseded

type UnknownState =
    | Identified
    | Investigating
    | UnknownResolved
    | AcceptedResidual
    | UnknownSuperseded

type Materiality =
    | Blocking
    | Material
    | NonMaterial
    | Informational

type AssumptionState =
    | ExplicitUnverified
    | ProvisionallyAccepted
    | AssumptionVerified
    | Rejected
    | Expired

type ContradictionState =
    | Detected
    | Triaged
    | ContradictionInvestigating
    | ContradictionResolved
    | ContradictionAcceptedResidual
    | ContradictionSuperseded

type ActionState =
    | ActionProposed
    | ActionApproved
    | ActionInProgress
    | ActionCompleted
    | ActionInterpreted
    | ActionRejected
    | ActionCancelled

type ActionKind =
    | Test
    | Inspection
    | Query
    | Comparison
    | Experiment

type CausalAssertionState =
    | CauseProposed
    | CauseConfirmed
    | CauseReopened
    | CauseSuperseded

type CauseRole =
    | RootCause
    | ContributingFactor
    | Trigger
    | EnablingCondition

type CorrectiveActionState =
    | CorrectionProposed
    | CorrectionApproved
    | CorrectionInProgress
    | Implemented
    | AwaitingVerificationOfEffect
    | VerifiedEffective
    | VerificationFailed
    | ReworkRequired
    | CorrectionCancelled

/// R7.2: temporary mitigation is distinguishable from root-cause correction.
type CorrectiveActionKind =
    | Mitigation
    | Workaround
    | Correction
    | Prevention

type EvidenceCategory =
    | DirectObservation
    | Measurement
    | Log
    | Document
    | ExperimentResult
    | Testimony
    | ExternalReference

type VerificationStatus =
    | Unverified
    | PartiallyVerified
    | Verified
    | Invalidated

/// R3.3 relationship kinds (FSHARP-TYPE-SHAPES §15 subset; OQ-012 open).
type RelationshipKind =
    | Supports
    | Contradicts
    | Discriminates
    | Insufficient

type PredictionStrength =
    | Necessary
    | Expected
    | DiscriminatingPrediction
    | NonDiscriminating

type PredictionTiming =
    | Prospective
    | Retrospective

type VerificationOutcome =
    | Satisfied
    | Failed
    | Inconclusive

type ConclusionStrength =
    | PlausibleExplanation
    | LeadingExplanation
    | SupportedCause
    | ConfirmedCause
    | AcceptedWithResidualUncertainty

type ProposalState =
    | ProposalActive
    | ProposalAccepted
    | ProposalRejected

type ProposalKind =
    | ProposedHypothesis
    | ProposedUnknown
    | ProposedAction

type EvidenceContent =
    | TextEvidence of NonEmptyText
    /// R3.5: binary artifacts are referenced by URL, never stored inline.
    | ArtifactEvidence of uri: string

/// One recorded hypothesis state change (R2.3: why, and which evidence).
type HypothesisChange =
    { From: HypothesisState
      To: HypothesisState
      Command: string
      Reason: NonEmptyText
      Evidence: EvidenceId list
      At: DateTimeOffset
      /// Event sequence of this change; "new evidence" means admitted later.
      Sequence: int64 }

type Observation =
    { Id: ObservationId
      /// R1.2: the original statement is preserved verbatim.
      Original: NonEmptyText
      ObservedAt: DateTimeOffset option
      Provenance: Provenance }

type Hypothesis =
    { Id: HypothesisId
      Statement: NonEmptyText
      Mechanism: NonEmptyText option
      State: HypothesisState
      History: HypothesisChange list
      Provenance: Provenance }

type Evidence =
    { Id: EvidenceId
      Content: EvidenceContent
      Category: EvidenceCategory
      /// R3.2 provenance: where it came from and when it was captured.
      Source: NonEmptyText
      CapturedAt: DateTimeOffset option
      Verification: VerificationStatus
      SupersededBy: EvidenceId option
      /// Sequence number of the event that admitted it (for "new evidence" checks).
      AdmittedAt: int64
      Provenance: Provenance }

type Relationship =
    { Id: RelationshipId
      Evidence: EvidenceId
      Hypothesis: HypothesisId
      Kind: RelationshipKind
      Rationale: NonEmptyText option
      Provenance: Provenance }

type Unknown =
    { Id: UnknownId
      Question: NonEmptyText
      Materiality: Materiality
      State: UnknownState
      Basis: NonEmptyText option
      Provenance: Provenance }

type Assumption =
    { Id: AssumptionId
      Proposition: NonEmptyText
      Materiality: Materiality
      State: AssumptionState
      Provenance: Provenance }

type Contradiction =
    { Id: ContradictionId
      Statement: NonEmptyText
      Evidence: EvidenceId list
      Materiality: Materiality
      State: ContradictionState
      Basis: NonEmptyText option
      Provenance: Provenance }

type Prediction =
    { Id: PredictionId
      Hypothesis: HypothesisId
      Statement: NonEmptyText
      Strength: PredictionStrength
      Timing: PredictionTiming
      Provenance: Provenance }

type InvestigationAction =
    { Id: ActionId
      Kind: ActionKind
      Purpose: NonEmptyText
      Targets: HypothesisId list
      State: ActionState
      Provenance: Provenance }

type Finding =
    { Id: FindingId
      Action: ActionId
      /// R4.4: the actual result exactly as entered.
      OriginalResult: NonEmptyText
      Interpretation: NonEmptyText option
      Provenance: Provenance }

type NegativeKnowledge =
    { Id: NegativeKnowledgeId
      Statement: NonEmptyText
      Scope: NonEmptyText
      Basis: EvidenceId list
      Provenance: Provenance }

type CausalMember = { Hypothesis: HypothesisId; Role: CauseRole }

type CausalAssertion =
    { Id: CausalAssertionId
      Outcome: NonEmptyText
      Members: CausalMember list
      Mechanism: NonEmptyText option
      State: CausalAssertionState
      Provenance: Provenance }

type CorrectiveAction =
    { Id: CorrectiveActionId
      Kind: CorrectiveActionKind
      Description: NonEmptyText
      Targets: CausalAssertionId list
      State: CorrectiveActionState
      /// Event sequence at which the current verification round began (0 = never).
      AwaitingSince: int64
      Provenance: Provenance }

type Criterion =
    { Id: CriterionId
      CorrectiveAction: CorrectiveActionId
      ExpectedOutcome: NonEmptyText
      Method: NonEmptyText option
      Provenance: Provenance }

type Verification =
    { Id: VerificationId
      Criterion: CriterionId
      Outcome: VerificationOutcome
      Evidence: EvidenceId list
      /// Event sequence that recorded it (verification freshness, R7.4).
      Sequence: int64
      Provenance: Provenance }

type Conclusion =
    { Id: ConclusionId
      Statement: NonEmptyText
      Strength: ConclusionStrength
      Causes: CausalAssertionId list
      ResidualUncertainty: NonEmptyText option
      /// Event sequence that recorded it; "latest conclusion" means highest sequence.
      Sequence: int64
      Provenance: Provenance }

type Proposal =
    { Id: ProposalId
      Kind: ProposalKind
      /// The proposed statement/question/purpose; never stored as evidence (R15.2).
      Text: NonEmptyText
      Rationale: NonEmptyText option
      State: ProposalState
      Decision: NonEmptyText option
      Provenance: Provenance }

/// Current projection of one investigation (FSHARP-TYPE-SHAPES §30).
/// History is the event log; this is the fold.
type Investigation =
    { Id: InvestigationId
      Title: NonEmptyText
      Lifecycle: InvestigationLifecycle
      Version: int64
      CreatedAt: DateTimeOffset
      Observations: Map<ObservationId, Observation>
      Hypotheses: Map<HypothesisId, Hypothesis>
      Evidence: Map<EvidenceId, Evidence>
      Relationships: Map<RelationshipId, Relationship>
      Unknowns: Map<UnknownId, Unknown>
      Assumptions: Map<AssumptionId, Assumption>
      Contradictions: Map<ContradictionId, Contradiction>
      Predictions: Map<PredictionId, Prediction>
      Actions: Map<ActionId, InvestigationAction>
      Findings: Map<FindingId, Finding>
      NegativeKnowledge: Map<NegativeKnowledgeId, NegativeKnowledge>
      Causes: Map<CausalAssertionId, CausalAssertion>
      CorrectiveActions: Map<CorrectiveActionId, CorrectiveAction>
      Criteria: Map<CriterionId, Criterion>
      Verifications: Map<VerificationId, Verification>
      Conclusions: Map<ConclusionId, Conclusion>
      Proposals: Map<ProposalId, Proposal> }
