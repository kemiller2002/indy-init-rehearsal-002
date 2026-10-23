namespace IndyInitRootCauseInvestigator.Engine

open System

/// Hypothesis lifecycle commands (no generic SetHypothesisState exists).
type HypothesisCommand =
    | MarkPlausible
    | MarkSupported
    | MarkWeakened
    | Falsify
    | Supersede
    | Reopen

type UnknownCommand =
    | StartInvestigatingUnknown
    | ResolveUnknown
    | AcceptUnknownAsResidual

type ContradictionCommand =
    | TriageContradiction
    | ResolveContradiction
    | AcceptContradictionAsResidual

type AssumptionCommand =
    | ProvisionallyAcceptAssumption
    | VerifyAssumption
    | RejectAssumption

type ActionCommand =
    | ApproveAction
    | RejectAction
    | StartAction
    | CancelAction

type CorrectionCommand =
    | ApproveCorrection
    | StartCorrection
    | MarkImplemented
    | StartVerificationOfEffect
    | RequireRework
    | CancelCorrection

/// Core commands (FSHARP-TYPE-SHAPES §31). Every id is chosen by the caller.
type Command =
    | StartInvestigation of id: InvestigationId * title: string * observation: ObservationId * originalText: string
    | ActivateInvestigation
    | MarkAwaitingEvidence of reason: string
    | MarkAwaitingVerification
    | ResolveInvestigation
    | CloseInvestigation
    | ReopenInvestigation of basis: string
    | RecordObservation of ObservationId * original: string * observedAt: DateTimeOffset option
    | RecordHypothesis of HypothesisId * statement: string * mechanism: string option
    | TransitionHypothesis of HypothesisId * HypothesisCommand * reason: string * evidence: EvidenceId list
    | AdmitEvidence of EvidenceId * content: EvidenceContentInput * EvidenceCategory * source: string * capturedAt: DateTimeOffset option
    | SupersedeEvidence of old: EvidenceId * replacement: EvidenceId * reason: string
    | VerifyEvidence of EvidenceId * VerificationStatus
    | RelateEvidence of RelationshipId * EvidenceId * HypothesisId * RelationshipKind * rationale: string option
    | IdentifyUnknown of UnknownId * question: string * Materiality
    | TransitionUnknown of UnknownId * UnknownCommand * basis: string
    | RecordAssumption of AssumptionId * proposition: string * Materiality
    | TransitionAssumption of AssumptionId * AssumptionCommand
    | RecordContradiction of ContradictionId * statement: string * evidence: EvidenceId list * Materiality
    | TransitionContradiction of ContradictionId * ContradictionCommand * basis: string
    | RecordPrediction of PredictionId * HypothesisId * statement: string * PredictionStrength * PredictionTiming
    | RecordAction of ActionId * ActionKind * purpose: string * targets: HypothesisId list
    | TransitionAction of ActionId * ActionCommand
    | RecordFinding of FindingId * ActionId * originalResult: string * interpretation: string option
    | EstablishNegativeKnowledge of NegativeKnowledgeId * statement: string * scope: string * basis: EvidenceId list
    | RecordCausalAssertion of CausalAssertionId * outcome: string * CausalMember list * mechanism: string option
    | ConfirmCause of CausalAssertionId
    | ReopenCause of CausalAssertionId * reason: string
    | RecordCorrectiveAction of CorrectiveActionId * CorrectiveActionKind * description: string * targets: CausalAssertionId list
    | TransitionCorrection of CorrectiveActionId * CorrectionCommand
    | RecordCriterion of CriterionId * CorrectiveActionId * expected: string * method: string option
    | RecordVerification of VerificationId * CriterionId * VerificationOutcome * evidence: EvidenceId list
    | RecordConclusion of ConclusionId * statement: string * ConclusionStrength * causes: CausalAssertionId list * residual: string option
    | RecordProposal of ProposalId * ProposalKind * text: string * rationale: string option
    | AcceptProposal of ProposalId * targetId: Guid
    | RejectProposal of ProposalId * reason: string

and EvidenceContentInput =
    | TextInput of string
    | UrlInput of string

type CommandEnvelope =
    { CommandId: Guid
      Investigation: InvestigationId
      ExpectedVersion: int64 option
      Actor: Actor
      RequestedAt: DateTimeOffset
      Command: Command }

/// Semantic events. Each carries everything `evolve` needs; validation
/// already happened in `decide`, so evolve is total.
type EventBody =
    | InvestigationStarted of title: NonEmptyText * observation: ObservationId * original: NonEmptyText
    | LifecycleChanged of from: InvestigationLifecycle * ``to``: InvestigationLifecycle * command: string * reason: string option
    | ObservationRecorded of ObservationId * NonEmptyText * DateTimeOffset option
    | HypothesisRecorded of HypothesisId * NonEmptyText * NonEmptyText option
    | HypothesisTransitioned of HypothesisId * HypothesisChange
    | EvidenceAdmitted of EvidenceId * EvidenceContent * EvidenceCategory * source: NonEmptyText * DateTimeOffset option
    | EvidenceSuperseded of old: EvidenceId * replacement: EvidenceId * reason: NonEmptyText
    | EvidenceVerificationAssessed of EvidenceId * VerificationStatus
    | EvidenceRelated of RelationshipId * EvidenceId * HypothesisId * RelationshipKind * NonEmptyText option
    | UnknownIdentified of UnknownId * NonEmptyText * Materiality
    | UnknownTransitioned of UnknownId * UnknownState * NonEmptyText
    | AssumptionRecorded of AssumptionId * NonEmptyText * Materiality
    | AssumptionTransitioned of AssumptionId * AssumptionState
    | ContradictionRecorded of ContradictionId * NonEmptyText * EvidenceId list * Materiality
    | ContradictionTransitioned of ContradictionId * ContradictionState * NonEmptyText
    | PredictionRecorded of PredictionId * HypothesisId * NonEmptyText * PredictionStrength * PredictionTiming
    | ActionRecorded of ActionId * ActionKind * NonEmptyText * HypothesisId list
    | ActionTransitioned of ActionId * ActionState
    | FindingRecorded of FindingId * ActionId * NonEmptyText * NonEmptyText option
    | NegativeKnowledgeEstablished of NegativeKnowledgeId * NonEmptyText * NonEmptyText * EvidenceId list
    | CausalAssertionRecorded of CausalAssertionId * NonEmptyText * CausalMember list * NonEmptyText option
    | CausalAssertionConfirmed of CausalAssertionId * policy: string
    | CausalAssertionReopened of CausalAssertionId * NonEmptyText
    | CorrectiveActionRecorded of CorrectiveActionId * CorrectiveActionKind * NonEmptyText * CausalAssertionId list
    | CorrectiveActionTransitioned of CorrectiveActionId * CorrectiveActionState
    | CriterionRecorded of CriterionId * CorrectiveActionId * NonEmptyText * NonEmptyText option
    | VerificationRecorded of VerificationId * CriterionId * VerificationOutcome * EvidenceId list
    | ConclusionRecorded of ConclusionId * NonEmptyText * ConclusionStrength * CausalAssertionId list * NonEmptyText option
    | ProposalRecorded of ProposalId * ProposalKind * NonEmptyText * NonEmptyText option
    | ProposalWasAccepted of ProposalId * targetId: Guid
    | ProposalWasRejected of ProposalId * NonEmptyText

type Event =
    { Sequence: int64
      Investigation: InvestigationId
      CommandId: Guid
      Actor: Actor
      At: DateTimeOffset
      Body: EventBody }

/// A typed, explainable refusal (R8.2). Mapped to an Aegis fault at the boundary (R8.3).
type DomainError =
    { Code: string
      Message: string
      /// The rule or contract clause that produced the refusal.
      Rule: string }

[<RequireQualifiedAccess>]
module DomainError =
    let create code rule message = { Code = code; Message = message; Rule = rule }
