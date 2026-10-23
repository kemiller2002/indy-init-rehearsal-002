namespace Causa.Engine

open System
open IndyInitRootCauseInvestigator.Engine

/// The commands the interface can submit. Each maps to exactly one domain `Command`; the domain decides legality
/// (Decide.execute). The interface renders the form the engine describes and never builds a command itself.
type CommandKind =
    | NewInvestigation
    | Activate
    | AwaitEvidence
    | AwaitVerification
    | Resolve
    | Close
    | ReopenInv
    | AddObservation
    | AddHypothesis
    | MoveHypothesis
    | AddPrediction
    | AddEvidence
    | AssessEvidence
    | ReplaceEvidence
    | RelateFromHypothesis
    | RelateFromEvidence
    | AddUnknown
    | MoveUnknown
    | AddAssumption
    | MoveAssumption
    | AddContradiction
    | MoveContradiction
    | AddAction
    | MoveAction
    | AddFinding
    | AddNegativeKnowledge
    | AddCause
    | ConfirmCauseCmd
    | ReopenCauseCmd
    | AddCorrection
    | MoveCorrection
    | AddCriterion
    | AddVerification
    | AddConclusion
    | RejectProposalCmd

/// The fixed form slots the page provides. A slot is shown only when the engine labels it.
type Slot =
    | TextA
    | TextB
    | EnumA
    | EnumB
    | RefA
    | RefsB

type Field =
    { Slot: Slot
      Label: string
      Required: bool
      Help: string }

/// A command form: what it is called, which slots it uses, the options each choice slot offers (all engine-derived),
/// and whether it needs an explicit confirmation because it is irreversible or freezes the investigation.
type CommandForm =
    { Kind: CommandKind
      Title: string
      Fields: Field list
      Options: Map<Slot, (string * string) list>
      Confirm: string option
      Target: string option }

type FormDraft =
    { Values: Map<Slot, string>
      Selected: Set<string> }

[<RequireQualifiedAccess>]
module FormDraft =
    let empty = { Values = Map.empty; Selected = Set.empty }
    let get slot (d: FormDraft) = d.Values |> Map.tryFind slot |> Option.defaultValue ""

[<RequireQualifiedAccess>]
module CommandKind =
    let all =
        [ NewInvestigation; Activate; AwaitEvidence; AwaitVerification; Resolve; Close; ReopenInv; AddObservation
          AddHypothesis; MoveHypothesis; AddPrediction; AddEvidence; AssessEvidence; ReplaceEvidence; RelateFromHypothesis
          RelateFromEvidence; AddUnknown; MoveUnknown; AddAssumption; MoveAssumption; AddContradiction; MoveContradiction
          AddAction; MoveAction; AddFinding; AddNegativeKnowledge; AddCause; ConfirmCauseCmd; ReopenCauseCmd; AddCorrection
          MoveCorrection; AddCriterion; AddVerification; AddConclusion; RejectProposalCmd ]

    let key (k: CommandKind) =
        match k with
        | NewInvestigation -> "new-investigation" | Activate -> "activate" | AwaitEvidence -> "await-evidence"
        | AwaitVerification -> "await-verification" | Resolve -> "resolve" | Close -> "close" | ReopenInv -> "reopen"
        | AddObservation -> "add-observation" | AddHypothesis -> "add-hypothesis" | MoveHypothesis -> "move-hypothesis"
        | AddPrediction -> "add-prediction" | AddEvidence -> "add-evidence" | AssessEvidence -> "assess-evidence"
        | ReplaceEvidence -> "replace-evidence" | RelateFromHypothesis -> "relate-from-hypothesis"
        | RelateFromEvidence -> "relate-from-evidence" | AddUnknown -> "add-unknown" | MoveUnknown -> "move-unknown"
        | AddAssumption -> "add-assumption" | MoveAssumption -> "move-assumption" | AddContradiction -> "add-contradiction"
        | MoveContradiction -> "move-contradiction" | AddAction -> "add-action" | MoveAction -> "move-action"
        | AddFinding -> "add-finding" | AddNegativeKnowledge -> "add-negative-knowledge" | AddCause -> "add-cause"
        | ConfirmCauseCmd -> "confirm-cause" | ReopenCauseCmd -> "reopen-cause" | AddCorrection -> "add-correction"
        | MoveCorrection -> "move-correction" | AddCriterion -> "add-criterion" | AddVerification -> "add-verification"
        | AddConclusion -> "add-conclusion" | RejectProposalCmd -> "reject-proposal"

    let tryParse (s: string) = all |> List.tryFind (fun k -> key k = s)

    let label (k: CommandKind) =
        match k with
        | NewInvestigation -> "New investigation" | Activate -> "Activate investigation" | AwaitEvidence -> "Mark awaiting evidence"
        | AwaitVerification -> "Mark awaiting verification" | Resolve -> "Resolve investigation" | Close -> "Close investigation"
        | ReopenInv -> "Reopen investigation" | AddObservation -> "Record observation" | AddHypothesis -> "Record hypothesis"
        | MoveHypothesis -> "Change hypothesis state" | AddPrediction -> "Record prediction" | AddEvidence -> "Admit evidence"
        | AssessEvidence -> "Assess verification" | ReplaceEvidence -> "Supersede evidence" | RelateFromHypothesis -> "Relate evidence"
        | RelateFromEvidence -> "Relate to hypothesis" | AddUnknown -> "Identify unknown" | MoveUnknown -> "Change unknown state"
        | AddAssumption -> "Record assumption" | MoveAssumption -> "Change assumption state" | AddContradiction -> "Record contradiction"
        | MoveContradiction -> "Change contradiction state" | AddAction -> "Propose action" | MoveAction -> "Change action state"
        | AddFinding -> "Record finding" | AddNegativeKnowledge -> "Establish negative knowledge" | AddCause -> "Propose cause"
        | ConfirmCauseCmd -> "Confirm cause" | ReopenCauseCmd -> "Reopen cause" | AddCorrection -> "Propose corrective action"
        | MoveCorrection -> "Change corrective action state" | AddCriterion -> "Add verification criterion"
        | AddVerification -> "Record verification" | AddConclusion -> "Record conclusion" | RejectProposalCmd -> "Reject proposal"

/// Wire names and human labels for domain enumerations (contract §6 lowerCamelCase values).
[<RequireQualifiedAccess>]
module Labels =
    let lifecycle =
        function
        | Draft -> "Draft" | Active -> "Active" | AwaitingEvidence -> "Awaiting evidence"
        | AwaitingVerification -> "Awaiting verification" | Resolved -> "Resolved" | Closed -> "Closed" | Archived -> "Archived"

    let hypothesis =
        function
        | Proposed -> "Proposed" | Plausible -> "Plausible" | Supported -> "Supported" | Weakened -> "Weakened"
        | Falsified -> "Falsified" | Superseded -> "Superseded"

    let hypothesisCommand =
        function
        | MarkPlausible -> "Mark plausible" | MarkSupported -> "Mark supported" | MarkWeakened -> "Mark weakened"
        | Falsify -> "Falsify" | Supersede -> "Supersede" | HypothesisCommand.Reopen -> "Reopen"

    let unknown =
        function
        | Identified -> "Identified" | Investigating -> "Investigating" | UnknownResolved -> "Resolved"
        | AcceptedResidual -> "Accepted as residual" | UnknownSuperseded -> "Superseded"

    let materiality =
        function
        | Blocking -> "Blocking" | Material -> "Material" | NonMaterial -> "Non-material" | Informational -> "Informational"

    let assumption =
        function
        | ExplicitUnverified -> "Unverified" | ProvisionallyAccepted -> "Provisionally accepted" | AssumptionVerified -> "Verified"
        | Rejected -> "Rejected" | Expired -> "Expired"

    let contradiction =
        function
        | Detected -> "Detected" | Triaged -> "Triaged" | ContradictionInvestigating -> "Investigating"
        | ContradictionResolved -> "Resolved" | ContradictionAcceptedResidual -> "Accepted as residual" | ContradictionSuperseded -> "Superseded"

    let action =
        function
        | ActionProposed -> "Proposed" | ActionApproved -> "Approved" | ActionInProgress -> "In progress"
        | ActionCompleted -> "Completed" | ActionInterpreted -> "Interpreted" | ActionRejected -> "Rejected" | ActionCancelled -> "Cancelled"

    let actionKind =
        function
        | Test -> "Test" | Inspection -> "Inspection" | Query -> "Query" | Comparison -> "Comparison" | Experiment -> "Experiment"

    let cause =
        function
        | CauseProposed -> "Proposed" | CauseConfirmed -> "Confirmed" | CauseReopened -> "Reopened" | CauseSuperseded -> "Superseded"

    let role =
        function
        | RootCause -> "Root cause" | ContributingFactor -> "Contributing factor" | Trigger -> "Trigger" | EnablingCondition -> "Enabling condition"

    let correction =
        function
        | CorrectionProposed -> "Proposed" | CorrectionApproved -> "Approved" | CorrectionInProgress -> "In progress"
        | Implemented -> "Implemented" | AwaitingVerificationOfEffect -> "Awaiting verification of effect"
        | VerifiedEffective -> "Verified effective" | VerificationFailed -> "Verification failed"
        | ReworkRequired -> "Rework required" | CorrectionCancelled -> "Cancelled"

    let correctionKind =
        function
        | Mitigation -> "Mitigation" | Workaround -> "Workaround" | Correction -> "Correction" | Prevention -> "Prevention"

    let category =
        function
        | DirectObservation -> "Direct observation" | Measurement -> "Measurement" | Log -> "Log" | Document -> "Document"
        | ExperimentResult -> "Experiment result" | Testimony -> "Testimony" | ExternalReference -> "External reference"

    let verificationStatus =
        function
        | Unverified -> "Unverified" | PartiallyVerified -> "Partially verified" | Verified -> "Verified" | Invalidated -> "Invalidated"

    let relationship =
        function
        | Supports -> "Supports" | Contradicts -> "Contradicts" | Discriminates -> "Discriminates" | Insufficient -> "Insufficient"

    let strength =
        function
        | Necessary -> "Necessary" | Expected -> "Expected" | DiscriminatingPrediction -> "Discriminating" | NonDiscriminating -> "Non-discriminating"

    let timing = function Prospective -> "Prospective" | Retrospective -> "Retrospective"

    let outcome = function Satisfied -> "Satisfied" | Failed -> "Failed" | Inconclusive -> "Inconclusive"

    let conclusion =
        function
        | PlausibleExplanation -> "Plausible explanation" | LeadingExplanation -> "Leading explanation"
        | SupportedCause -> "Supported cause" | ConfirmedCause -> "Confirmed cause"
        | AcceptedWithResidualUncertainty -> "Accepted with residual uncertainty"

    let proposalKind = function ProposedHypothesis -> "Hypothesis" | ProposedUnknown -> "Unknown" | ProposedAction -> "Action"
    let proposalState = function ProposalActive -> "Active" | ProposalAccepted -> "Accepted" | ProposalRejected -> "Rejected"

    let unknownCommand =
        function
        | StartInvestigatingUnknown -> "Start investigating" | ResolveUnknown -> "Resolve" | AcceptUnknownAsResidual -> "Accept as residual"
    let contradictionCommand =
        function
        | TriageContradiction -> "Triage" | ResolveContradiction -> "Resolve" | AcceptContradictionAsResidual -> "Accept as residual"
    let assumptionCommand =
        function
        | ProvisionallyAcceptAssumption -> "Provisionally accept" | VerifyAssumption -> "Verify" | RejectAssumption -> "Reject"
    let actionCommand = function ApproveAction -> "Approve" | RejectAction -> "Reject" | StartAction -> "Start" | CancelAction -> "Cancel"
    let correctionCommand =
        function
        | ApproveCorrection -> "Approve" | StartCorrection -> "Start" | MarkImplemented -> "Mark implemented"
        | StartVerificationOfEffect -> "Start verification of effect" | RequireRework -> "Require rework" | CancelCorrection -> "Cancel"

    let text (t: NonEmptyText) = t.Value
    let optText (t: NonEmptyText option) = t |> Option.map (fun x -> x.Value) |> Option.defaultValue ""
    let shortId (g: Guid) = (Ids.text g).Substring(0, 8)

/// Entity references a form may cite, by kind (values are canonical UUID text, labels are verbatim domain text).
[<RequireQualifiedAccess>]
module Choices =
    let private clip (s: string) = if s.Length > 90 then s.Substring(0, 89) + "…" else s
    let hypotheses (inv: Investigation) =
        inv.Hypotheses |> Map.toList |> List.map (fun (id, h) -> Ids.text id.Value, clip h.Statement.Value + " (" + Labels.hypothesis h.State + ")")
    let evidence (inv: Investigation) =
        inv.Evidence
        |> Map.toList
        |> List.map (fun (id, e) ->
            let content = match e.Content with TextEvidence t -> t.Value | ArtifactEvidence u -> u
            Ids.text id.Value, clip content + " — " + clip e.Source.Value)
    let causes (inv: Investigation) =
        inv.Causes |> Map.toList |> List.map (fun (id, c) -> Ids.text id.Value, clip c.Outcome.Value + " (" + Labels.cause c.State + ")")

    let enumOf (label: 'T -> string) (values: 'T list) = values |> List.map (fun v -> (sprintf "%A" v), label v)

[<RequireQualifiedAccess>]
module CommandForms =
    let private field slot label required help = { Slot = slot; Label = label; Required = required; Help = help }
    let private form kind fields options confirm target =
        { Kind = kind; Title = CommandKind.label kind; Fields = fields; Options = Map.ofList options; Confirm = confirm; Target = target }

    let materialities = [ Blocking; Material; NonMaterial; Informational ]
    let categories = [ DirectObservation; Measurement; Log; Document; ExperimentResult; Testimony; ExternalReference ]
    let roles = [ RootCause; ContributingFactor; Trigger; EnablingCondition ]
    let relationshipKinds = [ Supports; Contradicts; Discriminates; Insufficient ]

    let private enumOptions label values = values |> List.map (fun v -> (sprintf "%A" v), label v)

    let private parseEnum (values: 'T list) (raw: string) = values |> List.tryFind (fun v -> sprintf "%A" v = raw)

    /// The legal next states of an entity come from the domain's own transition tables (Transitions).
    let hypothesisMoves (h: Hypothesis) =
        [ MarkPlausible; MarkSupported; MarkWeakened; Falsify; Supersede; HypothesisCommand.Reopen ]
        |> List.filter (fun c -> (Transitions.hypothesis h.State c).IsSome)
    let unknownMoves (u: Unknown) =
        [ StartInvestigatingUnknown; ResolveUnknown; AcceptUnknownAsResidual ] |> List.filter (fun c -> (Transitions.unknown u.State c).IsSome)
    let assumptionMoves (a: Assumption) =
        [ ProvisionallyAcceptAssumption; VerifyAssumption; RejectAssumption ] |> List.filter (fun c -> (Transitions.assumption a.State c).IsSome)
    let contradictionMoves (c: Contradiction) =
        [ TriageContradiction; ResolveContradiction; AcceptContradictionAsResidual ] |> List.filter (fun x -> (Transitions.contradiction c.State x).IsSome)
    let actionMoves (a: InvestigationAction) =
        [ ApproveAction; RejectAction; StartAction; CancelAction ] |> List.filter (fun c -> (Transitions.action a.State c).IsSome)
    let correctionMoves (c: CorrectiveAction) =
        [ ApproveCorrection; StartCorrection; MarkImplemented; StartVerificationOfEffect; RequireRework; CancelCorrection ]
        |> List.filter (fun x -> (Transitions.correction c.State x).IsSome)

    /// The form for a command, given the investigation (if any) and the entity it targets (if any).
    let describe (inv: Investigation option) (kind: CommandKind) (target: Guid option) : CommandForm =
        let tgt = target |> Option.map Ids.text
        let hyps = inv |> Option.map Choices.hypotheses |> Option.defaultValue []
        let evs = inv |> Option.map Choices.evidence |> Option.defaultValue []
        let cs = inv |> Option.map Choices.causes |> Option.defaultValue []
        let findIn (m: Investigation -> Map<Id<'T>, 'E>) = match inv, target with Some i, Some g -> m i |> Map.tryFind (Id.create g) | _ -> None
        match kind with
        | NewInvestigation ->
            form kind [ field TextA "Title" true "A short name for what is being investigated."
                        field TextB "Initial observation" true "What was observed, in the words of whoever observed it. Stored verbatim." ] [] None None
        | Activate -> form kind [] [] None None
        | AwaitEvidence -> form kind [ field TextB "Reason" true "What evidence is awaited and why." ] [] None None
        | AwaitVerification -> form kind [] [] None None
        | Resolve -> form kind [] [] (Some "Resolving freezes the investigation's content. Resolve only when the domain's resolution gate is satisfied.") None
        | Close -> form kind [] [] (Some "Closing records that the investigation is finished.") None
        | ReopenInv -> form kind [ field TextB "Basis for reopening" true "" ] [] None None
        | AddObservation -> form kind [ field TextB "Observation" true "Stored verbatim." ] [] None None
        | AddHypothesis ->
            form kind [ field TextB "Statement" true "A candidate explanation."; field TextA "Mechanism" false "How it would produce the observation (optional)." ] [] None None
        | MoveHypothesis ->
            let moves = findIn (fun i -> i.Hypotheses) |> Option.map hypothesisMoves |> Option.defaultValue []
            form kind [ field EnumA "Transition" true "Only transitions the domain's table allows from the current state are offered."
                        field TextB "Reason" true ""
                        field RefsB "Cited evidence" false "Supported, weakened, falsified and reopen require cited evidence." ]
                [ EnumA, enumOptions Labels.hypothesisCommand moves; RefsB, evs ] None tgt
        | AddPrediction ->
            form kind [ field TextB "Prediction" true "What the hypothesis predicts."
                        field EnumA "Strength" true ""
                        field EnumB "Timing requested" true "The domain decides the recorded timing (a prediction after relevant evidence is retrospective)." ]
                [ EnumA, enumOptions Labels.strength [ Necessary; Expected; DiscriminatingPrediction; NonDiscriminating ]
                  EnumB, enumOptions Labels.timing [ Prospective; Retrospective ] ] None tgt
        | AddEvidence ->
            form kind [ field TextB "Content" true "Text is stored verbatim. For an artifact, give an absolute http(s) URL."
                        field EnumA "Content kind" true ""
                        field EnumB "Category" true ""
                        field TextA "Source" true "Where the evidence came from (provenance)." ]
                [ EnumA, [ "Text", "Text"; "Artifact", "Artifact URL" ]; EnumB, enumOptions Labels.category categories ] None None
        | AssessEvidence ->
            form kind [ field EnumA "Verification status" true "" ]
                [ EnumA, enumOptions Labels.verificationStatus [ Unverified; PartiallyVerified; Verified; Invalidated ] ] None tgt
        | ReplaceEvidence ->
            form kind [ field RefA "Replacement evidence" true "The original stays in history."; field TextB "Reason" true "" ]
                [ RefA, evs |> List.filter (fun (v, _) -> Some v <> tgt) ] None tgt
        | RelateFromHypothesis ->
            form kind [ field RefA "Evidence" true ""; field EnumA "Relationship" true ""; field TextA "Rationale" false "" ]
                [ RefA, evs; EnumA, enumOptions Labels.relationship relationshipKinds ] None tgt
        | RelateFromEvidence ->
            form kind [ field RefA "Hypothesis" true ""; field EnumA "Relationship" true ""; field TextA "Rationale" false "" ]
                [ RefA, hyps; EnumA, enumOptions Labels.relationship relationshipKinds ] None tgt
        | AddUnknown ->
            form kind [ field TextB "Question" true "An open question that matters to the conclusion."; field EnumA "Materiality" true "" ]
                [ EnumA, enumOptions Labels.materiality materialities ] None None
        | MoveUnknown ->
            let moves = findIn (fun i -> i.Unknowns) |> Option.map unknownMoves |> Option.defaultValue []
            form kind [ field EnumA "Transition" true ""; field TextB "Basis" true "" ] [ EnumA, enumOptions Labels.unknownCommand moves ] None tgt
        | AddAssumption ->
            form kind [ field TextB "Proposition" true ""; field EnumA "Materiality" true "" ] [ EnumA, enumOptions Labels.materiality materialities ] None None
        | MoveAssumption ->
            let moves = findIn (fun i -> i.Assumptions) |> Option.map assumptionMoves |> Option.defaultValue []
            form kind [ field EnumA "Transition" true "" ] [ EnumA, enumOptions Labels.assumptionCommand moves ] None tgt
        | AddContradiction ->
            form kind [ field TextB "Statement" true "What conflicts."; field RefsB "Conflicting evidence" true ""; field EnumA "Materiality" true "" ]
                [ RefsB, evs; EnumA, enumOptions Labels.materiality materialities ] None None
        | MoveContradiction ->
            let moves = findIn (fun i -> i.Contradictions) |> Option.map contradictionMoves |> Option.defaultValue []
            form kind [ field EnumA "Transition" true ""; field TextB "Basis" true "" ] [ EnumA, enumOptions Labels.contradictionCommand moves ] None tgt
        | AddAction ->
            form kind [ field EnumA "Kind" true ""; field TextB "Purpose" true ""; field RefsB "Hypotheses it tests" false "" ]
                [ EnumA, enumOptions Labels.actionKind [ Test; Inspection; Query; Comparison; Experiment ]; RefsB, hyps ] None None
        | MoveAction ->
            let moves = findIn (fun i -> i.Actions) |> Option.map actionMoves |> Option.defaultValue []
            form kind [ field EnumA "Transition" true "" ] [ EnumA, enumOptions Labels.actionCommand moves ] None tgt
        | AddFinding ->
            form kind [ field TextB "Actual result" true "Exactly as observed; stored verbatim."; field TextA "Interpretation" false "" ] [] None tgt
        | AddNegativeKnowledge ->
            form kind [ field TextB "What is established not to be the case" true ""; field TextA "Scope" true ""; field RefsB "Basis evidence" true "" ]
                [ RefsB, evs ] None None
        | AddCause ->
            form kind [ field TextB "Outcome explained" true ""; field TextA "Mechanism" false ""
                        field RefsB "Member hypotheses" true "Each selected hypothesis joins with the role below."; field EnumA "Role" true "" ]
                [ RefsB, hyps; EnumA, enumOptions Labels.role roles ] None None
        | ConfirmCauseCmd -> form kind [] [] (Some "Confirmation is decided by the domain's cause gate and recorded permanently.") tgt
        | ReopenCauseCmd -> form kind [ field TextB "Reason" true "" ] [] None tgt
        | AddCorrection ->
            form kind [ field EnumA "Kind" true "Mitigation is distinct from correction."; field TextB "Description" true ""; field RefsB "Causes addressed" false "" ]
                [ EnumA, enumOptions Labels.correctionKind [ Mitigation; Workaround; Correction; Prevention ]; RefsB, cs ] None None
        | MoveCorrection ->
            let moves = findIn (fun i -> i.CorrectiveActions) |> Option.map correctionMoves |> Option.defaultValue []
            form kind [ field EnumA "Transition" true "" ] [ EnumA, enumOptions Labels.correctionCommand moves ] None tgt
        | AddCriterion -> form kind [ field TextB "Expected outcome" true ""; field TextA "Method" false "" ] [] None tgt
        | AddVerification ->
            form kind [ field EnumA "Outcome" true ""; field RefsB "Evidence" false "A satisfied outcome must cite evidence." ]
                [ EnumA, enumOptions Labels.outcome [ Satisfied; Failed; Inconclusive ]; RefsB, evs ] None tgt
        | AddConclusion ->
            form kind [ field TextB "Statement" true ""; field EnumA "Strength" true ""; field RefsB "Causes" false ""
                        field TextA "Residual uncertainty" false "What remains unknown." ]
                [ EnumA, enumOptions Labels.conclusion [ PlausibleExplanation; LeadingExplanation; SupportedCause; ConfirmedCause; AcceptedWithResidualUncertainty ]
                  RefsB, cs ] None None
        | RejectProposalCmd -> form kind [ field TextB "Reason" true "" ] [] None tgt

    // ------------------------------------------------------------------------------------------------------------
    // Building the domain command from a draft. Validation here is input shape only (required, parseable); legality
    // is decided by Decide.execute.
    // ------------------------------------------------------------------------------------------------------------

    let private required (label: string) (value: string) = if String.IsNullOrWhiteSpace value then Error(label + " is required.") else Ok value
    let private optional (value: string) = if String.IsNullOrWhiteSpace value then None else Some value
    let private choose (label: string) values raw = match parseEnum values raw with Some v -> Ok v | None -> Error("Choose " + label + ".")
    let private ids (draft: FormDraft) = draft.Selected |> Set.toList |> List.choose (fun s -> match Guid.TryParse s with true, g -> Some g | _ -> None)
    let private ref' (label: string) (raw: string) = match Guid.TryParse raw with true, g -> Ok g | _ -> Error("Choose " + label + ".")
    let private targetId (target: Guid option) = match target with Some g -> Ok g | None -> Error "No entity is selected."

    let private result2 a b f = match a, b with Ok x, Ok y -> Ok(f x y) | Error e, _ | _, Error e -> Error e
    let private result3 a b c f = match a, b, c with Ok x, Ok y, Ok z -> Ok(f x y z) | Error e, _, _ | _, Error e, _ | _, _, Error e -> Error e
    let private result4 a b c d f =
        match a, b, c, d with
        | Ok w, Ok x, Ok y, Ok z -> Ok(f w x y z)
        | Error e, _, _, _ | _, Error e, _, _ | _, _, Error e, _ | _, _, _, Error e -> Error e

    /// `fresh n` gives the n-th new identifier for this command (entropy from the boundary).
    let build (kind: CommandKind) (target: Guid option) (draft: FormDraft) (fresh: int -> Guid) : Result<Command, string> =
        let v slot = FormDraft.get slot draft
        let id n : Id<'T> = Id.create (fresh n)
        match kind with
        | NewInvestigation ->
            result2 (required "Title" (v TextA)) (required "Initial observation" (v TextB)) (fun title original ->
                StartInvestigation(id 1, title, id 2, original))
        | Activate -> Ok ActivateInvestigation
        | AwaitEvidence -> required "Reason" (v TextB) |> Result.map MarkAwaitingEvidence
        | AwaitVerification -> Ok MarkAwaitingVerification
        | Resolve -> Ok ResolveInvestigation
        | Close -> Ok CloseInvestigation
        | ReopenInv -> required "Basis" (v TextB) |> Result.map ReopenInvestigation
        | AddObservation -> required "Observation" (v TextB) |> Result.map (fun o -> RecordObservation(id 1, o, None))
        | AddHypothesis -> required "Statement" (v TextB) |> Result.map (fun s -> RecordHypothesis(id 1, s, optional (v TextA)))
        | MoveHypothesis ->
            result3 (targetId target) (choose "a transition" [ MarkPlausible; MarkSupported; MarkWeakened; Falsify; Supersede; HypothesisCommand.Reopen ] (v EnumA)) (required "Reason" (v TextB))
                (fun t c r -> TransitionHypothesis(Id.create t, c, r, ids draft |> List.map Id.create))
        | AddPrediction ->
            result4 (targetId target) (required "Prediction" (v TextB)) (choose "a strength" [ Necessary; Expected; DiscriminatingPrediction; NonDiscriminating ] (v EnumA))
                (choose "a timing" [ Prospective; Retrospective ] (v EnumB))
                (fun t s st tm -> RecordPrediction(id 1, Id.create t, s, st, tm))
        | AddEvidence ->
            result4 (required "Content" (v TextB)) (match v EnumA with "Text" -> Ok false | "Artifact" -> Ok true | _ -> Error "Choose a content kind.")
                (choose "a category" categories (v EnumB)) (required "Source" (v TextA))
                (fun content isUrl cat source -> AdmitEvidence(id 1, (if isUrl then UrlInput content else TextInput content), cat, source, None))
        | AssessEvidence ->
            result2 (targetId target) (choose "a verification status" [ Unverified; PartiallyVerified; Verified; Invalidated ] (v EnumA))
                (fun t s -> VerifyEvidence(Id.create t, s))
        | ReplaceEvidence ->
            result3 (targetId target) (ref' "replacement evidence" (v RefA)) (required "Reason" (v TextB))
                (fun t r reason -> SupersedeEvidence(Id.create t, Id.create r, reason))
        | RelateFromHypothesis ->
            result3 (targetId target) (ref' "evidence" (v RefA)) (choose "a relationship" relationshipKinds (v EnumA))
                (fun h e k -> RelateEvidence(id 1, Id.create e, Id.create h, k, optional (v TextA)))
        | RelateFromEvidence ->
            result3 (targetId target) (ref' "a hypothesis" (v RefA)) (choose "a relationship" relationshipKinds (v EnumA))
                (fun e h k -> RelateEvidence(id 1, Id.create e, Id.create h, k, optional (v TextA)))
        | AddUnknown ->
            result2 (required "Question" (v TextB)) (choose "a materiality" materialities (v EnumA)) (fun q m -> IdentifyUnknown(id 1, q, m))
        | MoveUnknown ->
            result3 (targetId target) (choose "a transition" [ StartInvestigatingUnknown; ResolveUnknown; AcceptUnknownAsResidual ] (v EnumA)) (required "Basis" (v TextB))
                (fun t c b -> TransitionUnknown(Id.create t, c, b))
        | AddAssumption ->
            result2 (required "Proposition" (v TextB)) (choose "a materiality" materialities (v EnumA)) (fun p m -> RecordAssumption(id 1, p, m))
        | MoveAssumption ->
            result2 (targetId target) (choose "a transition" [ ProvisionallyAcceptAssumption; VerifyAssumption; RejectAssumption ] (v EnumA))
                (fun t c -> TransitionAssumption(Id.create t, c))
        | AddContradiction ->
            result3 (required "Statement" (v TextB)) (choose "a materiality" materialities (v EnumA))
                (match ids draft with [] -> Error "Select the conflicting evidence." | xs -> Ok xs)
                (fun s m evs -> RecordContradiction(id 1, s, evs |> List.map Id.create, m))
        | MoveContradiction ->
            result3 (targetId target) (choose "a transition" [ TriageContradiction; ResolveContradiction; AcceptContradictionAsResidual ] (v EnumA)) (required "Basis" (v TextB))
                (fun t c b -> TransitionContradiction(Id.create t, c, b))
        | AddAction ->
            result2 (choose "a kind" [ Test; Inspection; Query; Comparison; Experiment ] (v EnumA)) (required "Purpose" (v TextB))
                (fun k p -> RecordAction(id 1, k, p, ids draft |> List.map Id.create))
        | MoveAction ->
            result2 (targetId target) (choose "a transition" [ ApproveAction; RejectAction; StartAction; CancelAction ] (v EnumA))
                (fun t c -> TransitionAction(Id.create t, c))
        | AddFinding ->
            result2 (targetId target) (required "Actual result" (v TextB)) (fun t r -> RecordFinding(id 1, Id.create t, r, optional (v TextA)))
        | AddNegativeKnowledge ->
            result3 (required "Statement" (v TextB)) (required "Scope" (v TextA)) (match ids draft with [] -> Error "Select basis evidence." | xs -> Ok xs)
                (fun s sc evs -> EstablishNegativeKnowledge(id 1, s, sc, evs |> List.map Id.create))
        | AddCause ->
            result3 (required "Outcome" (v TextB)) (choose "a role" roles (v EnumA)) (match ids draft with [] -> Error "Select at least one member hypothesis." | xs -> Ok xs)
                (fun o role hs -> RecordCausalAssertion(id 1, o, hs |> List.map (fun h -> { Hypothesis = Id.create h; Role = role }), optional (v TextA)))
        | ConfirmCauseCmd -> targetId target |> Result.map (Id.create >> ConfirmCause)
        | ReopenCauseCmd -> result2 (targetId target) (required "Reason" (v TextB)) (fun t r -> ReopenCause(Id.create t, r))
        | AddCorrection ->
            result2 (choose "a kind" [ Mitigation; Workaround; Correction; Prevention ] (v EnumA)) (required "Description" (v TextB))
                (fun k d -> RecordCorrectiveAction(id 1, k, d, ids draft |> List.map Id.create))
        | MoveCorrection ->
            result2 (targetId target)
                (choose "a transition" [ ApproveCorrection; StartCorrection; MarkImplemented; StartVerificationOfEffect; RequireRework; CancelCorrection ] (v EnumA))
                (fun t c -> TransitionCorrection(Id.create t, c))
        | AddCriterion -> result2 (targetId target) (required "Expected outcome" (v TextB)) (fun t e -> RecordCriterion(id 1, Id.create t, e, optional (v TextA)))
        | AddVerification ->
            result2 (targetId target) (choose "an outcome" [ Satisfied; Failed; Inconclusive ] (v EnumA))
                (fun t o -> RecordVerification(id 1, Id.create t, o, ids draft |> List.map Id.create))
        | AddConclusion ->
            result2 (required "Statement" (v TextB))
                (choose "a strength" [ PlausibleExplanation; LeadingExplanation; SupportedCause; ConfirmedCause; AcceptedWithResidualUncertainty ] (v EnumA))
                (fun s st -> RecordConclusion(id 1, s, st, ids draft |> List.map Id.create, optional (v TextA)))
        | RejectProposalCmd -> result2 (targetId target) (required "Reason" (v TextB)) (fun t r -> RejectProposal(Id.create t, r))
