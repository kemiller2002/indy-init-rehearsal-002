namespace Causa.Engine

open System
open IndyInitRootCauseInvestigator.Engine

/// What the interface may offer now, and why something is not available. Every verdict comes from the domain:
/// `Decide.probe` (the same decision path `Decide.execute` uses) or the domain's `Transitions` tables. The browser
/// never computes legality (UI-004, ADRP-004).
type Availability =
    { Kind: CommandKind
      Enabled: bool
      Reason: string }

type EntityKindView =
    | HypothesisView
    | EvidenceView
    | UnknownView
    | AssumptionView
    | ContradictionView
    | ActionView
    | CauseView
    | CorrectionView
    | CriterionView
    | ProposalView
    | ObservationView
    | PredictionView
    | FindingView
    | VerificationView
    | ConclusionView
    | NegativeView

[<RequireQualifiedAccess>]
module Capabilities =
    let investigationCommands =
        [ AddHypothesis; AddEvidence; AddObservation; AddUnknown; AddAssumption; AddContradiction; AddAction
          AddNegativeKnowledge; AddCause; AddCorrection; AddConclusion; Activate; AwaitEvidence; AwaitVerification
          Resolve; Close; ReopenInv ]

    let private placeholder = "x"
    let private sample (n: int) : Id<'T> = Id.create (Guid(n, 0s, 0s, [| 0uy; 0uy; 0uy; 0uy; 0uy; 0uy; 0uy; byte n |]))
    let private firstKey (m: Map<Id<'T>, 'V>) = m |> Map.toList |> List.tryHead |> Option.map fst

    /// A representative command the domain can judge. `None` when the form needs a prerequisite that does not exist.
    let private representative (inv: Investigation) (kind: CommandKind) (target: Guid option) : Result<Command, string> =
        let t () = match target with Some g -> Ok g | None -> Error "Select an item first."
        let evidence = inv.Evidence |> Map.toList |> List.map fst
        match kind with
        | NewInvestigation -> Error "Not an investigation command."
        | Activate -> Ok ActivateInvestigation
        | AwaitEvidence -> Ok(MarkAwaitingEvidence placeholder)
        | AwaitVerification -> Ok MarkAwaitingVerification
        | Resolve -> Ok ResolveInvestigation
        | Close -> Ok CloseInvestigation
        | ReopenInv -> Ok(ReopenInvestigation placeholder)
        | AddObservation -> Ok(RecordObservation(sample 1, placeholder, None))
        | AddHypothesis -> Ok(RecordHypothesis(sample 1, placeholder, None))
        | AddEvidence -> Ok(AdmitEvidence(sample 1, TextInput placeholder, DirectObservation, placeholder, None))
        | AddUnknown -> Ok(IdentifyUnknown(sample 1, placeholder, Informational))
        | AddAssumption -> Ok(RecordAssumption(sample 1, placeholder, Informational))
        | AddContradiction ->
            if evidence.IsEmpty then Error "Admit evidence before recording a contradiction."
            else Ok(RecordContradiction(sample 1, placeholder, [ evidence.Head ], Informational))
        | AddAction -> Ok(RecordAction(sample 1, Test, placeholder, []))
        | AddNegativeKnowledge ->
            if evidence.IsEmpty then Error "Admit evidence before establishing negative knowledge."
            else Ok(EstablishNegativeKnowledge(sample 1, placeholder, placeholder, [ evidence.Head ]))
        | AddCause ->
            match firstKey inv.Hypotheses with
            | None -> Error "Record a hypothesis before proposing a cause."
            | Some h -> Ok(RecordCausalAssertion(sample 1, placeholder, [ { Hypothesis = h; Role = RootCause } ], None))
        | AddCorrection -> Ok(RecordCorrectiveAction(sample 1, Mitigation, placeholder, []))
        | AddConclusion -> Ok(RecordConclusion(sample 1, placeholder, PlausibleExplanation, [], Some placeholder))
        | ConfirmCauseCmd -> t () |> Result.map (Id.create >> ConfirmCause)
        | ReopenCauseCmd -> t () |> Result.map (fun g -> ReopenCause(Id.create g, placeholder))
        | RejectProposalCmd -> t () |> Result.map (fun g -> RejectProposal(Id.create g, placeholder))
        | AddFinding -> t () |> Result.map (fun g -> RecordFinding(sample 1, Id.create g, placeholder, None))
        | AddCriterion -> t () |> Result.map (fun g -> RecordCriterion(sample 1, Id.create g, placeholder, None))
        | AddVerification -> t () |> Result.map (fun g -> RecordVerification(sample 1, Id.create g, Inconclusive, []))
        | AddPrediction -> t () |> Result.map (fun g -> RecordPrediction(sample 1, Id.create g, placeholder, Expected, Prospective))
        | AssessEvidence -> t () |> Result.map (fun g -> VerifyEvidence(Id.create g, PartiallyVerified))
        | RelateFromHypothesis ->
            match t (), evidence with
            | Ok g, e :: _ -> Ok(RelateEvidence(sample 1, e, Id.create g, Supports, None))
            | Ok _, [] -> Error "Admit evidence first."
            | Error e, _ -> Error e
        | RelateFromEvidence ->
            match t (), firstKey inv.Hypotheses with
            | Ok g, Some h -> Ok(RelateEvidence(sample 1, Id.create g, h, Supports, None))
            | Ok _, None -> Error "Record a hypothesis first."
            | Error e, _ -> Error e
        | ReplaceEvidence ->
            match t () with
            | Ok g ->
                match evidence |> List.tryFind (fun e -> e.Value <> g) with
                | Some r -> Ok(SupersedeEvidence(Id.create g, r, placeholder))
                | None -> Error "Admit the replacement evidence first."
            | Error e -> Error e
        // Entity transitions: offered when the domain's transition table allows at least one move from the
        // current state; the exact command (and its evidence rules) is decided at submission.
        | MoveHypothesis | MoveUnknown | MoveAssumption | MoveContradiction | MoveAction | MoveCorrection -> Error "transition"

    let private moves (inv: Investigation) (kind: CommandKind) (target: Guid option) : int =
        match target with
        | None -> 0
        | Some g ->
            match kind with
            | MoveHypothesis -> inv.Hypotheses |> Map.tryFind (Id.create g) |> Option.map (CommandForms.hypothesisMoves >> List.length) |> Option.defaultValue 0
            | MoveUnknown -> inv.Unknowns |> Map.tryFind (Id.create g) |> Option.map (CommandForms.unknownMoves >> List.length) |> Option.defaultValue 0
            | MoveAssumption -> inv.Assumptions |> Map.tryFind (Id.create g) |> Option.map (CommandForms.assumptionMoves >> List.length) |> Option.defaultValue 0
            | MoveContradiction -> inv.Contradictions |> Map.tryFind (Id.create g) |> Option.map (CommandForms.contradictionMoves >> List.length) |> Option.defaultValue 0
            | MoveAction -> inv.Actions |> Map.tryFind (Id.create g) |> Option.map (CommandForms.actionMoves >> List.length) |> Option.defaultValue 0
            | MoveCorrection -> inv.CorrectiveActions |> Map.tryFind (Id.create g) |> Option.map (CommandForms.correctionMoves >> List.length) |> Option.defaultValue 0
            | _ -> 0

    let private firstMessage (errors: DomainError list) =
        match errors with
        | e :: _ -> e.Message
        | [] -> "Not permitted."

    /// Judge one command for the current investigation and (optionally) a selected entity.
    let judge (writeGate: Result<unit, string>) (now: DateTimeOffset) (inv: Investigation) (kind: CommandKind) (target: Guid option) : Availability =
        let verdict =
            match writeGate with
            | Error reason -> Error reason
            | Ok() ->
                match kind with
                | MoveHypothesis | MoveUnknown | MoveAssumption | MoveContradiction | MoveAction | MoveCorrection ->
                    if not (Transitions.isOpen inv.Lifecycle) then Error "The investigation is not open; its content is frozen."
                    elif moves inv kind target = 0 then Error "No transition is allowed from the current state."
                    else Ok()
                | _ ->
                    representative inv kind target
                    |> Result.bind (fun command -> Decide.probe inv Session.actor (Ids.truncate now) command |> Result.mapError firstMessage)
        match verdict with
        | Ok() -> { Kind = kind; Enabled = true; Reason = "" }
        | Error reason -> { Kind = kind; Enabled = false; Reason = reason }

    /// The commands that apply to a selected entity of a given kind.
    let entityCommands (kind: EntityKindView) : CommandKind list =
        match kind with
        | HypothesisView -> [ MoveHypothesis; RelateFromHypothesis; AddPrediction ]
        | EvidenceView -> [ AssessEvidence; RelateFromEvidence; ReplaceEvidence ]
        | UnknownView -> [ MoveUnknown ]
        | AssumptionView -> [ MoveAssumption ]
        | ContradictionView -> [ MoveContradiction ]
        | ActionView -> [ MoveAction; AddFinding ]
        | CauseView -> [ ConfirmCauseCmd; ReopenCauseCmd ]
        | CorrectionView -> [ MoveCorrection; AddCriterion ]
        | CriterionView -> [ AddVerification ]
        | ProposalView -> [ RejectProposalCmd ]
        | ObservationView | PredictionView | FindingView | VerificationView | ConclusionView | NegativeView -> []

    /// The next legal lifecycle step, as the domain judges it now (shown in the investigation header).
    let nextLifecycle (now: DateTimeOffset) (inv: Investigation) : string =
        [ Activate; AwaitVerification; Resolve; Close; ReopenInv ]
        |> List.map (fun k -> k, judge (Ok()) now inv k None)
        |> List.tryFind (fun (_, a) -> a.Enabled)
        |> Option.map (fun (k, _) -> CommandKind.label k)
        |> Option.defaultValue "No lifecycle step is currently permitted"
