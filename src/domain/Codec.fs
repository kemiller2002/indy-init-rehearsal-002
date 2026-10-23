namespace IndyInitRootCauseInvestigator.Engine

open System
open System.Collections.Generic
open System.Globalization
open System.Security.Cryptography
open System.Text
open System.Text.Encodings.Web
open System.Text.Json
open System.Text.Json.Nodes

/// Minimal Result computation expression shared by the persistence slice.
[<AutoOpen>]
module internal ResultExpression =
    type ResultBuilder() =
        member _.Bind(r: Result<'A, 'E>, f: 'A -> Result<'B, 'E>) = Result.bind f r
        member _.Return(x: 'A) : Result<'A, 'E> = Ok x
        member _.ReturnFrom(r: Result<'A, 'E>) = r

    let result = ResultBuilder()

    /// First error wins; order is preserved.
    let rec traverse (f: 'A -> Result<'B, 'E>) (xs: 'A list) : Result<'B list, 'E> =
        match xs with
        | [] -> Ok []
        | x :: rest ->
            match f x with
            | Error e -> Error e
            | Ok y -> traverse f rest |> Result.map (fun ys -> y :: ys)

/// Hand-written, stable persisted names for every domain union case.
/// No reflection (`%A`, `ToString` on unions) is used: the engine runs trimmed.
[<RequireQualifiedAccess>]
module CaseNames =
    let lifecycle =
        function
        | Draft -> "Draft"
        | Active -> "Active"
        | AwaitingEvidence -> "AwaitingEvidence"
        | AwaitingVerification -> "AwaitingVerification"
        | Resolved -> "Resolved"
        | Closed -> "Closed"
        | Archived -> "Archived"

    let allLifecycles = [ Draft; Active; AwaitingEvidence; AwaitingVerification; Resolved; Closed; Archived ]

    let hypothesisState =
        function
        | Proposed -> "Proposed"
        | Plausible -> "Plausible"
        | Supported -> "Supported"
        | Weakened -> "Weakened"
        | Falsified -> "Falsified"
        | Superseded -> "Superseded"

    let allHypothesisStates = [ Proposed; Plausible; Supported; Weakened; Falsified; Superseded ]

    let unknownState =
        function
        | Identified -> "Identified"
        | Investigating -> "Investigating"
        | UnknownResolved -> "UnknownResolved"
        | AcceptedResidual -> "AcceptedResidual"
        | UnknownSuperseded -> "UnknownSuperseded"

    let allUnknownStates = [ Identified; Investigating; UnknownResolved; AcceptedResidual; UnknownSuperseded ]

    let materiality =
        function
        | Blocking -> "Blocking"
        | Material -> "Material"
        | NonMaterial -> "NonMaterial"
        | Informational -> "Informational"

    let allMaterialities = [ Blocking; Material; NonMaterial; Informational ]

    let assumptionState =
        function
        | ExplicitUnverified -> "ExplicitUnverified"
        | ProvisionallyAccepted -> "ProvisionallyAccepted"
        | AssumptionVerified -> "AssumptionVerified"
        | Rejected -> "Rejected"
        | Expired -> "Expired"

    let allAssumptionStates = [ ExplicitUnverified; ProvisionallyAccepted; AssumptionVerified; Rejected; Expired ]

    let contradictionState =
        function
        | Detected -> "Detected"
        | Triaged -> "Triaged"
        | ContradictionInvestigating -> "ContradictionInvestigating"
        | ContradictionResolved -> "ContradictionResolved"
        | ContradictionAcceptedResidual -> "ContradictionAcceptedResidual"
        | ContradictionSuperseded -> "ContradictionSuperseded"

    let allContradictionStates =
        [ Detected; Triaged; ContradictionInvestigating; ContradictionResolved; ContradictionAcceptedResidual; ContradictionSuperseded ]

    let actionState =
        function
        | ActionProposed -> "ActionProposed"
        | ActionApproved -> "ActionApproved"
        | ActionInProgress -> "ActionInProgress"
        | ActionCompleted -> "ActionCompleted"
        | ActionInterpreted -> "ActionInterpreted"
        | ActionRejected -> "ActionRejected"
        | ActionCancelled -> "ActionCancelled"

    let allActionStates =
        [ ActionProposed; ActionApproved; ActionInProgress; ActionCompleted; ActionInterpreted; ActionRejected; ActionCancelled ]

    let actionKind =
        function
        | Test -> "Test"
        | Inspection -> "Inspection"
        | Query -> "Query"
        | ActionKind.Comparison -> "Comparison"
        | Experiment -> "Experiment"

    let allActionKinds = [ Test; Inspection; Query; ActionKind.Comparison; Experiment ]

    let causeState =
        function
        | CauseProposed -> "CauseProposed"
        | CauseConfirmed -> "CauseConfirmed"
        | CauseReopened -> "CauseReopened"
        | CauseSuperseded -> "CauseSuperseded"

    let allCauseStates = [ CauseProposed; CauseConfirmed; CauseReopened; CauseSuperseded ]

    let causeRole =
        function
        | RootCause -> "RootCause"
        | ContributingFactor -> "ContributingFactor"
        | Trigger -> "Trigger"
        | EnablingCondition -> "EnablingCondition"

    let allCauseRoles = [ RootCause; ContributingFactor; Trigger; EnablingCondition ]

    let correctionState =
        function
        | CorrectionProposed -> "CorrectionProposed"
        | CorrectionApproved -> "CorrectionApproved"
        | CorrectionInProgress -> "CorrectionInProgress"
        | Implemented -> "Implemented"
        | AwaitingVerificationOfEffect -> "AwaitingVerificationOfEffect"
        | VerifiedEffective -> "VerifiedEffective"
        | VerificationFailed -> "VerificationFailed"
        | ReworkRequired -> "ReworkRequired"
        | CorrectionCancelled -> "CorrectionCancelled"

    let allCorrectionStates =
        [ CorrectionProposed; CorrectionApproved; CorrectionInProgress; Implemented; AwaitingVerificationOfEffect
          VerifiedEffective; VerificationFailed; ReworkRequired; CorrectionCancelled ]

    let correctionKind =
        function
        | Mitigation -> "Mitigation"
        | Workaround -> "Workaround"
        | Correction -> "Correction"
        | Prevention -> "Prevention"

    let allCorrectionKinds = [ Mitigation; Workaround; Correction; Prevention ]

    let evidenceCategory =
        function
        | DirectObservation -> "DirectObservation"
        | Measurement -> "Measurement"
        | Log -> "Log"
        | Document -> "Document"
        | ExperimentResult -> "ExperimentResult"
        | Testimony -> "Testimony"
        | ExternalReference -> "ExternalReference"

    let allEvidenceCategories = [ DirectObservation; Measurement; Log; Document; ExperimentResult; Testimony; ExternalReference ]

    let verificationStatus =
        function
        | Unverified -> "Unverified"
        | PartiallyVerified -> "PartiallyVerified"
        | Verified -> "Verified"
        | Invalidated -> "Invalidated"

    let allVerificationStatuses = [ Unverified; PartiallyVerified; Verified; Invalidated ]

    let relationshipKind =
        function
        | Supports -> "Supports"
        | Contradicts -> "Contradicts"
        | Discriminates -> "Discriminates"
        | Insufficient -> "Insufficient"

    let allRelationshipKinds = [ Supports; Contradicts; Discriminates; Insufficient ]

    let predictionStrength =
        function
        | Necessary -> "Necessary"
        | Expected -> "Expected"
        | DiscriminatingPrediction -> "DiscriminatingPrediction"
        | NonDiscriminating -> "NonDiscriminating"

    let allPredictionStrengths = [ Necessary; Expected; DiscriminatingPrediction; NonDiscriminating ]

    let predictionTiming =
        function
        | Prospective -> "Prospective"
        | Retrospective -> "Retrospective"

    let allPredictionTimings = [ Prospective; Retrospective ]

    let verificationOutcome =
        function
        | Satisfied -> "Satisfied"
        | Failed -> "Failed"
        | Inconclusive -> "Inconclusive"

    let allVerificationOutcomes = [ Satisfied; Failed; Inconclusive ]

    let conclusionStrength =
        function
        | PlausibleExplanation -> "PlausibleExplanation"
        | LeadingExplanation -> "LeadingExplanation"
        | SupportedCause -> "SupportedCause"
        | ConfirmedCause -> "ConfirmedCause"
        | AcceptedWithResidualUncertainty -> "AcceptedWithResidualUncertainty"

    let allConclusionStrengths =
        [ PlausibleExplanation; LeadingExplanation; SupportedCause; ConfirmedCause; AcceptedWithResidualUncertainty ]

    let proposalKind =
        function
        | ProposedHypothesis -> "ProposedHypothesis"
        | ProposedUnknown -> "ProposedUnknown"
        | ProposedAction -> "ProposedAction"

    let allProposalKinds = [ ProposedHypothesis; ProposedUnknown; ProposedAction ]

    let proposalState =
        function
        | ProposalActive -> "ProposalActive"
        | ProposalAccepted -> "ProposalAccepted"
        | ProposalRejected -> "ProposalRejected"

    /// The persisted "type" of an event: the EventBody case name.
    let eventType =
        function
        | InvestigationStarted _ -> "InvestigationStarted"
        | LifecycleChanged _ -> "LifecycleChanged"
        | ObservationRecorded _ -> "ObservationRecorded"
        | HypothesisRecorded _ -> "HypothesisRecorded"
        | HypothesisTransitioned _ -> "HypothesisTransitioned"
        | EvidenceAdmitted _ -> "EvidenceAdmitted"
        | EvidenceSuperseded _ -> "EvidenceSuperseded"
        | EvidenceVerificationAssessed _ -> "EvidenceVerificationAssessed"
        | EvidenceRelated _ -> "EvidenceRelated"
        | UnknownIdentified _ -> "UnknownIdentified"
        | UnknownTransitioned _ -> "UnknownTransitioned"
        | AssumptionRecorded _ -> "AssumptionRecorded"
        | AssumptionTransitioned _ -> "AssumptionTransitioned"
        | ContradictionRecorded _ -> "ContradictionRecorded"
        | ContradictionTransitioned _ -> "ContradictionTransitioned"
        | PredictionRecorded _ -> "PredictionRecorded"
        | ActionRecorded _ -> "ActionRecorded"
        | ActionTransitioned _ -> "ActionTransitioned"
        | FindingRecorded _ -> "FindingRecorded"
        | NegativeKnowledgeEstablished _ -> "NegativeKnowledgeEstablished"
        | CausalAssertionRecorded _ -> "CausalAssertionRecorded"
        | CausalAssertionConfirmed _ -> "CausalAssertionConfirmed"
        | CausalAssertionReopened _ -> "CausalAssertionReopened"
        | CorrectiveActionRecorded _ -> "CorrectiveActionRecorded"
        | CorrectiveActionTransitioned _ -> "CorrectiveActionTransitioned"
        | CriterionRecorded _ -> "CriterionRecorded"
        | VerificationRecorded _ -> "VerificationRecorded"
        | ConclusionRecorded _ -> "ConclusionRecorded"
        | ProposalRecorded _ -> "ProposalRecorded"
        | ProposalWasAccepted _ -> "ProposalWasAccepted"
        | ProposalWasRejected _ -> "ProposalWasRejected"

    /// "AwaitingVerificationOfEffect" -> "Awaiting verification of effect".
    let humanize (name: string) =
        name
        |> Seq.mapi (fun i c -> if i > 0 && Char.IsUpper c then " " + string (Char.ToLowerInvariant c) else string c)
        |> String.concat ""

/// Low-level JSON building/reading over System.Text.Json.Nodes (no reflection).
module internal Json =
    let options =
        JsonSerializerOptions(WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, NewLine = "\n")

    let nn (node: JsonNode | null) : JsonNode =
        match node with
        | null -> invalidOp "unexpected null JSON node"
        | n -> n

    let private up (n: JsonNode) : JsonNode | null = n
    let str (s: string) : JsonNode | null = up (nonNull (JsonValue.Create(s)))
    let i64 (n: int64) : JsonNode | null = up (JsonValue.Create(n))
    let date (d: DateTimeOffset) = str (d.ToString("O", CultureInfo.InvariantCulture))
    let optDate (d: DateTimeOffset option) = d |> Option.map date |> Option.defaultValue null
    let text (t: NonEmptyText) = str t.Value
    let optText (t: NonEmptyText option) = t |> Option.map text |> Option.defaultValue null
    let id (i: Id<'Tag>) = str (Id.text i)
    let guid (g: Guid) = str (g.ToString("D"))

    let obj (fields: (string * (JsonNode | null)) list) : JsonNode | null =
        up (JsonObject(fields |> List.map (fun (k, v) -> KeyValuePair<string, JsonNode | null>(k, v))))

    let arr (items: (JsonNode | null) list) : JsonNode | null = up (JsonArray(Array.ofList items))
    let ids (xs: Id<'Tag> list) = arr (xs |> List.map id)

    let render (node: JsonNode | null) = (nn node).ToJsonString(options)

    let parse (text: string) : Result<JsonNode, string> =
        try
            match JsonNode.Parse(text) with
            | null -> Error "the document is empty (null)"
            | n -> Ok n
        with :? JsonException as e -> Error("malformed JSON: " + e.Message)

    let asObject (what: string) (node: JsonNode | null) : Result<JsonObject, string> =
        match node with
        | :? JsonObject as o -> Ok o
        | _ -> Error(what + " must be a JSON object")

    let asArray (what: string) (node: JsonNode | null) : Result<JsonArray, string> =
        match node with
        | :? JsonArray as a -> Ok a
        | _ -> Error(what + " must be a JSON array")

    /// A field that may be absent or null.
    let tryField (o: JsonObject) (name: string) : JsonNode option =
        match o[name] with
        | null -> None
        | n -> Some n

    let field (o: JsonObject) (name: string) : Result<JsonNode, string> =
        match tryField o name with
        | Some n -> Ok n
        | None -> Error("missing field '" + name + "'")

    let asString (name: string) (n: JsonNode) : Result<string, string> =
        match n.GetValueKind() with
        | JsonValueKind.String -> Ok(n.GetValue<string>())
        | _ -> Error("field '" + name + "' must be a string")

    let getString o name = field o name |> Result.bind (asString name)

    let getOptString o name =
        match tryField o name with
        | None -> Ok None
        | Some n -> asString name n |> Result.map Some

    let getInt64 (o: JsonObject) (name: string) : Result<int64, string> =
        field o name
        |> Result.bind (fun n ->
            match n.GetValueKind() with
            | JsonValueKind.Number ->
                match Int64.TryParse(n.ToJsonString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) with
                | true, v -> Ok v
                | _ -> Error("field '" + name + "' must be an integer")
            | _ -> Error("field '" + name + "' must be a number"))

    let getText o name : Result<NonEmptyText, string> =
        getString o name |> Result.bind (fun s -> NonEmptyText.create s |> Result.mapError (fun _ -> "field '" + name + "' must not be empty"))

    let getOptText o name : Result<NonEmptyText option, string> =
        getOptString o name
        |> Result.bind (function
            | None -> Ok None
            | Some s -> NonEmptyText.create s |> Result.map Some |> Result.mapError (fun _ -> "field '" + name + "' must not be empty"))

    let parseDate (name: string) (s: string) =
        match DateTimeOffset.TryParseExact(s, "O", CultureInfo.InvariantCulture, DateTimeStyles.None) with
        | true, d -> Ok d
        | _ -> Error("field '" + name + "' must be an ISO-8601 round-trip timestamp")

    let getDate o name = getString o name |> Result.bind (parseDate name)

    let getOptDate o name =
        getOptString o name
        |> Result.bind (function
            | None -> Ok None
            | Some s -> parseDate name s |> Result.map Some)

    let parseGuid (name: string) (s: string) =
        match Guid.TryParse s with
        | true, g -> Ok g
        | _ -> Error("field '" + name + "' must be a GUID")

    let getGuid o name = getString o name |> Result.bind (parseGuid name)

    let parseId<'Tag> (name: string) (s: string) : Result<Id<'Tag>, string> =
        match Id.tryParse<'Tag> s with
        | Some i -> Ok i
        | None -> Error("field '" + name + "' must be a non-empty GUID")

    let getId<'Tag> o name : Result<Id<'Tag>, string> = getString o name |> Result.bind (parseId<'Tag> name)

    let getIds<'Tag> o name : Result<Id<'Tag> list, string> =
        field o name
        |> Result.bind (asArray name)
        |> Result.bind (fun a -> a |> List.ofSeq |> traverse (fun n -> n |> Option.ofObj |> Option.map (asString name) |> Option.defaultValue (Error("field '" + name + "' contains null")) |> Result.bind (parseId<'Tag> name)))

    let getEnum (all: 'T list) (nameOf: 'T -> string) o name : Result<'T, string> =
        getString o name
        |> Result.bind (fun s ->
            match all |> List.tryFind (fun v -> nameOf v = s) with
            | Some v -> Ok v
            | None -> Error("field '" + name + "' has unknown value '" + s + "'"))

/// Canonical event document (PERSISTENCE-CONTRACT §4–§6).
[<RequireQualifiedAccess>]
module EventCodec =
    open Json

    let schema = "rci.events/v1"

    /// Every persisted event type, in EventBody declaration order.
    let typeName (body: EventBody) = CaseNames.eventType body

    let private actor (a: Actor) =
        match a.Kind with
        | Human -> obj [ "id", str a.Id; "kind", str "human" ]
        | System -> obj [ "id", str a.Id; "kind", str "system" ]
        | Agent(provider, model) -> obj [ "id", str a.Id; "kind", str "agent"; "provider", str provider; "model", str model ]

    let private data (body: EventBody) : (string * (JsonNode | null)) list =
        match body with
        | InvestigationStarted(title, oid, original) -> [ "title", text title; "observation", id oid; "original", text original ]
        | LifecycleChanged(from, target, command, reason) ->
            [ "from", str (CaseNames.lifecycle from); "to", str (CaseNames.lifecycle target); "command", str command
              "reason", (reason |> Option.map str |> Option.defaultValue null) ]
        | ObservationRecorded(oid, original, at) -> [ "observation", id oid; "original", text original; "observedAt", optDate at ]
        | HypothesisRecorded(hid, statement, mechanism) -> [ "hypothesis", id hid; "statement", text statement; "mechanism", optText mechanism ]
        | HypothesisTransitioned(hid, c) ->
            [ "hypothesis", id hid; "from", str (CaseNames.hypothesisState c.From); "to", str (CaseNames.hypothesisState c.To)
              "command", str c.Command; "reason", text c.Reason; "evidence", ids c.Evidence; "at", date c.At; "sequence", i64 c.Sequence ]
        | EvidenceAdmitted(eid, content, category, source, captured) ->
            let c =
                match content with
                | TextEvidence t -> obj [ "kind", str "text"; "text", text t ]
                | ArtifactEvidence uri -> obj [ "kind", str "artifact"; "uri", str uri ]
            [ "evidence", id eid; "content", c; "category", str (CaseNames.evidenceCategory category); "source", text source
              "capturedAt", optDate captured ]
        | EvidenceSuperseded(old, replacement, reason) -> [ "old", id old; "replacement", id replacement; "reason", text reason ]
        | EvidenceVerificationAssessed(eid, status) -> [ "evidence", id eid; "verification", str (CaseNames.verificationStatus status) ]
        | EvidenceRelated(rid, eid, hid, kind, rationale) ->
            [ "relationship", id rid; "evidence", id eid; "hypothesis", id hid; "kind", str (CaseNames.relationshipKind kind)
              "rationale", optText rationale ]
        | UnknownIdentified(uid, question, m) -> [ "unknown", id uid; "question", text question; "materiality", str (CaseNames.materiality m) ]
        | UnknownTransitioned(uid, state, basis) -> [ "unknown", id uid; "state", str (CaseNames.unknownState state); "basis", text basis ]
        | AssumptionRecorded(aid, prop, m) -> [ "assumption", id aid; "proposition", text prop; "materiality", str (CaseNames.materiality m) ]
        | AssumptionTransitioned(aid, state) -> [ "assumption", id aid; "state", str (CaseNames.assumptionState state) ]
        | ContradictionRecorded(cid, statement, evidence, m) ->
            [ "contradiction", id cid; "statement", text statement; "evidence", ids evidence; "materiality", str (CaseNames.materiality m) ]
        | ContradictionTransitioned(cid, state, basis) ->
            [ "contradiction", id cid; "state", str (CaseNames.contradictionState state); "basis", text basis ]
        | PredictionRecorded(pid, hid, statement, strength, timing) ->
            [ "prediction", id pid; "hypothesis", id hid; "statement", text statement
              "strength", str (CaseNames.predictionStrength strength); "timing", str (CaseNames.predictionTiming timing) ]
        | ActionRecorded(aid, kind, purpose, targets) ->
            [ "action", id aid; "kind", str (CaseNames.actionKind kind); "purpose", text purpose; "targets", ids targets ]
        | ActionTransitioned(aid, state) -> [ "action", id aid; "state", str (CaseNames.actionState state) ]
        | FindingRecorded(fid, aid, result, interpretation) ->
            [ "finding", id fid; "action", id aid; "originalResult", text result; "interpretation", optText interpretation ]
        | NegativeKnowledgeEstablished(nid, statement, scope, basis) ->
            [ "negativeKnowledge", id nid; "statement", text statement; "scope", text scope; "basis", ids basis ]
        | CausalAssertionRecorded(cid, outcome, members, mechanism) ->
            let ms = members |> List.map (fun m -> obj [ "hypothesis", id m.Hypothesis; "role", str (CaseNames.causeRole m.Role) ])
            [ "cause", id cid; "outcome", text outcome; "members", arr ms; "mechanism", optText mechanism ]
        | CausalAssertionConfirmed(cid, policy) -> [ "cause", id cid; "policy", str policy ]
        | CausalAssertionReopened(cid, reason) -> [ "cause", id cid; "reason", text reason ]
        | CorrectiveActionRecorded(caid, kind, description, targets) ->
            [ "correctiveAction", id caid; "kind", str (CaseNames.correctionKind kind); "description", text description; "targets", ids targets ]
        | CorrectiveActionTransitioned(caid, state) -> [ "correctiveAction", id caid; "state", str (CaseNames.correctionState state) ]
        | CriterionRecorded(crid, caid, expected, method) ->
            [ "criterion", id crid; "correctiveAction", id caid; "expectedOutcome", text expected; "method", optText method ]
        | VerificationRecorded(vid, crid, outcome, evidence) ->
            [ "verification", id vid; "criterion", id crid; "outcome", str (CaseNames.verificationOutcome outcome); "evidence", ids evidence ]
        | ConclusionRecorded(cid, statement, strength, causes, residual) ->
            [ "conclusion", id cid; "statement", text statement; "strength", str (CaseNames.conclusionStrength strength)
              "causes", ids causes; "residualUncertainty", optText residual ]
        | ProposalRecorded(pid, kind, t, rationale) ->
            [ "proposal", id pid; "kind", str (CaseNames.proposalKind kind); "text", text t; "rationale", optText rationale ]
        | ProposalWasAccepted(pid, target) -> [ "proposal", id pid; "target", guid target ]
        | ProposalWasRejected(pid, reason) -> [ "proposal", id pid; "reason", text reason ]

    let private eventNode (e: Event) =
        obj
            [ "seq", i64 e.Sequence
              "commandId", guid e.CommandId
              "actor", actor e.Actor
              "at", date e.At
              "type", str (typeName e.Body)
              "data", obj (data e.Body) ]

    /// Canonical, deterministic, human-readable JSON document.
    let encode (investigation: InvestigationId) (events: Event list) : string =
        obj [ "schema", str schema; "investigation", id investigation; "events", arr (events |> List.map eventNode) ]
        |> render

    let private decodeActor (o: JsonObject) : Result<Actor, string> =
        result {
            let! aid = getString o "id"
            let! kind = getString o "kind"
            match kind with
            | "human" -> return { Id = aid; Kind = Human }
            | "system" -> return { Id = aid; Kind = System }
            | "agent" ->
                let! provider = getString o "provider"
                let! model = getString o "model"
                return { Id = aid; Kind = Agent(provider, model) }
            | other -> return! Error("unknown actor kind '" + other + "'")
        }

    let private decodeMember (n: JsonNode | null) : Result<CausalMember, string> =
        result {
            let! o = asObject "cause member" n
            let! h = getId<HypothesisTag> o "hypothesis"
            let! role = getEnum CaseNames.allCauseRoles CaseNames.causeRole o "role"
            return { Hypothesis = h; Role = role }
        }

    let private decodeContent (o: JsonObject) : Result<EvidenceContent, string> =
        result {
            let! kind = getString o "kind"
            match kind with
            | "text" ->
                let! t = getText o "text"
                return TextEvidence t
            | "artifact" ->
                let! uri = getString o "uri"
                return ArtifactEvidence uri
            | other -> return! Error("unknown evidence content kind '" + other + "'")
        }

    let private decodeBody (eventType: string) (sequence: int64) (d: JsonObject) : Result<EventBody, string> =
        let lifecycle = getEnum CaseNames.allLifecycles CaseNames.lifecycle d
        let materiality = getEnum CaseNames.allMaterialities CaseNames.materiality d "materiality"
        match eventType with
        | "InvestigationStarted" ->
            result {
                let! title = getText d "title"
                let! oid = getId d "observation"
                let! original = getText d "original"
                return InvestigationStarted(title, oid, original)
            }
        | "LifecycleChanged" ->
            result {
                let! from = lifecycle "from"
                let! target = lifecycle "to"
                let! command = getString d "command"
                let! reason = getOptString d "reason"
                return LifecycleChanged(from, target, command, reason)
            }
        | "ObservationRecorded" ->
            result {
                let! oid = getId d "observation"
                let! original = getText d "original"
                let! at = getOptDate d "observedAt"
                return ObservationRecorded(oid, original, at)
            }
        | "HypothesisRecorded" ->
            result {
                let! hid = getId d "hypothesis"
                let! statement = getText d "statement"
                let! mechanism = getOptText d "mechanism"
                return HypothesisRecorded(hid, statement, mechanism)
            }
        | "HypothesisTransitioned" ->
            result {
                let! hid = getId d "hypothesis"
                let! from = getEnum CaseNames.allHypothesisStates CaseNames.hypothesisState d "from"
                let! target = getEnum CaseNames.allHypothesisStates CaseNames.hypothesisState d "to"
                let! command = getString d "command"
                let! reason = getText d "reason"
                let! evidence = getIds d "evidence"
                let! at = getDate d "at"
                let! changeSequence =
                    match tryField d "sequence" with
                    | None -> Ok sequence
                    | Some _ -> getInt64 d "sequence"
                return
                    HypothesisTransitioned(
                        hid,
                        { From = from; To = target; Command = command; Reason = reason; Evidence = evidence; At = at; Sequence = changeSequence }
                    )
            }
        | "EvidenceAdmitted" ->
            result {
                let! eid = getId d "evidence"
                let! content = field d "content" |> Result.bind (asObject "content") |> Result.bind decodeContent
                let! category = getEnum CaseNames.allEvidenceCategories CaseNames.evidenceCategory d "category"
                let! source = getText d "source"
                let! captured = getOptDate d "capturedAt"
                return EvidenceAdmitted(eid, content, category, source, captured)
            }
        | "EvidenceSuperseded" ->
            result {
                let! old = getId d "old"
                let! replacement = getId d "replacement"
                let! reason = getText d "reason"
                return EvidenceSuperseded(old, replacement, reason)
            }
        | "EvidenceVerificationAssessed" ->
            result {
                let! eid = getId d "evidence"
                let! status = getEnum CaseNames.allVerificationStatuses CaseNames.verificationStatus d "verification"
                return EvidenceVerificationAssessed(eid, status)
            }
        | "EvidenceRelated" ->
            result {
                let! rid = getId d "relationship"
                let! eid = getId d "evidence"
                let! hid = getId d "hypothesis"
                let! kind = getEnum CaseNames.allRelationshipKinds CaseNames.relationshipKind d "kind"
                let! rationale = getOptText d "rationale"
                return EvidenceRelated(rid, eid, hid, kind, rationale)
            }
        | "UnknownIdentified" ->
            result {
                let! uid = getId d "unknown"
                let! question = getText d "question"
                let! m = materiality
                return UnknownIdentified(uid, question, m)
            }
        | "UnknownTransitioned" ->
            result {
                let! uid = getId d "unknown"
                let! state = getEnum CaseNames.allUnknownStates CaseNames.unknownState d "state"
                let! basis = getText d "basis"
                return UnknownTransitioned(uid, state, basis)
            }
        | "AssumptionRecorded" ->
            result {
                let! aid = getId d "assumption"
                let! prop = getText d "proposition"
                let! m = materiality
                return AssumptionRecorded(aid, prop, m)
            }
        | "AssumptionTransitioned" ->
            result {
                let! aid = getId d "assumption"
                let! state = getEnum CaseNames.allAssumptionStates CaseNames.assumptionState d "state"
                return AssumptionTransitioned(aid, state)
            }
        | "ContradictionRecorded" ->
            result {
                let! cid = getId d "contradiction"
                let! statement = getText d "statement"
                let! evidence = getIds d "evidence"
                let! m = materiality
                return ContradictionRecorded(cid, statement, evidence, m)
            }
        | "ContradictionTransitioned" ->
            result {
                let! cid = getId d "contradiction"
                let! state = getEnum CaseNames.allContradictionStates CaseNames.contradictionState d "state"
                let! basis = getText d "basis"
                return ContradictionTransitioned(cid, state, basis)
            }
        | "PredictionRecorded" ->
            result {
                let! pid = getId d "prediction"
                let! hid = getId d "hypothesis"
                let! statement = getText d "statement"
                let! strength = getEnum CaseNames.allPredictionStrengths CaseNames.predictionStrength d "strength"
                let! timing = getEnum CaseNames.allPredictionTimings CaseNames.predictionTiming d "timing"
                return PredictionRecorded(pid, hid, statement, strength, timing)
            }
        | "ActionRecorded" ->
            result {
                let! aid = getId d "action"
                let! kind = getEnum CaseNames.allActionKinds CaseNames.actionKind d "kind"
                let! purpose = getText d "purpose"
                let! targets = getIds d "targets"
                return ActionRecorded(aid, kind, purpose, targets)
            }
        | "ActionTransitioned" ->
            result {
                let! aid = getId d "action"
                let! state = getEnum CaseNames.allActionStates CaseNames.actionState d "state"
                return ActionTransitioned(aid, state)
            }
        | "FindingRecorded" ->
            result {
                let! fid = getId d "finding"
                let! aid = getId d "action"
                let! r = getText d "originalResult"
                let! interpretation = getOptText d "interpretation"
                return FindingRecorded(fid, aid, r, interpretation)
            }
        | "NegativeKnowledgeEstablished" ->
            result {
                let! nid = getId d "negativeKnowledge"
                let! statement = getText d "statement"
                let! scope = getText d "scope"
                let! basis = getIds d "basis"
                return NegativeKnowledgeEstablished(nid, statement, scope, basis)
            }
        | "CausalAssertionRecorded" ->
            result {
                let! cid = getId d "cause"
                let! outcome = getText d "outcome"
                let! members = field d "members" |> Result.bind (asArray "members") |> Result.bind (List.ofSeq >> traverse decodeMember)
                let! mechanism = getOptText d "mechanism"
                return CausalAssertionRecorded(cid, outcome, members, mechanism)
            }
        | "CausalAssertionConfirmed" ->
            result {
                let! cid = getId d "cause"
                let! policy = getString d "policy"
                return CausalAssertionConfirmed(cid, policy)
            }
        | "CausalAssertionReopened" ->
            result {
                let! cid = getId d "cause"
                let! reason = getText d "reason"
                return CausalAssertionReopened(cid, reason)
            }
        | "CorrectiveActionRecorded" ->
            result {
                let! caid = getId d "correctiveAction"
                let! kind = getEnum CaseNames.allCorrectionKinds CaseNames.correctionKind d "kind"
                let! description = getText d "description"
                let! targets = getIds d "targets"
                return CorrectiveActionRecorded(caid, kind, description, targets)
            }
        | "CorrectiveActionTransitioned" ->
            result {
                let! caid = getId d "correctiveAction"
                let! state = getEnum CaseNames.allCorrectionStates CaseNames.correctionState d "state"
                return CorrectiveActionTransitioned(caid, state)
            }
        | "CriterionRecorded" ->
            result {
                let! crid = getId d "criterion"
                let! caid = getId d "correctiveAction"
                let! expected = getText d "expectedOutcome"
                let! method = getOptText d "method"
                return CriterionRecorded(crid, caid, expected, method)
            }
        | "VerificationRecorded" ->
            result {
                let! vid = getId d "verification"
                let! crid = getId d "criterion"
                let! outcome = getEnum CaseNames.allVerificationOutcomes CaseNames.verificationOutcome d "outcome"
                let! evidence = getIds d "evidence"
                return VerificationRecorded(vid, crid, outcome, evidence)
            }
        | "ConclusionRecorded" ->
            result {
                let! cid = getId d "conclusion"
                let! statement = getText d "statement"
                let! strength = getEnum CaseNames.allConclusionStrengths CaseNames.conclusionStrength d "strength"
                let! causes = getIds d "causes"
                let! residual = getOptText d "residualUncertainty"
                return ConclusionRecorded(cid, statement, strength, causes, residual)
            }
        | "ProposalRecorded" ->
            result {
                let! pid = getId d "proposal"
                let! kind = getEnum CaseNames.allProposalKinds CaseNames.proposalKind d "kind"
                let! t = getText d "text"
                let! rationale = getOptText d "rationale"
                return ProposalRecorded(pid, kind, t, rationale)
            }
        | "ProposalWasAccepted" ->
            result {
                let! pid = getId d "proposal"
                let! target = getGuid d "target"
                return ProposalWasAccepted(pid, target)
            }
        | "ProposalWasRejected" ->
            result {
                let! pid = getId d "proposal"
                let! reason = getText d "reason"
                return ProposalWasRejected(pid, reason)
            }
        // D-011/D-012: an unknown event type is never silently dropped.
        | other -> Error("unknown event type '" + other + "'")

    let private decodeEvent (investigation: InvestigationId) (index: int) (node: JsonNode | null) : Result<Event, string> =
        let at = "event[" + string index + "]"
        result {
            let! o = asObject at node
            let! sequence = getInt64 o "seq"
            let! commandId = getGuid o "commandId"
            let! actor = field o "actor" |> Result.bind (asObject "actor") |> Result.bind decodeActor
            let! occurred = getDate o "at"
            let! eventType = getString o "type"
            let! d = field o "data" |> Result.bind (asObject "data")
            let! body = decodeBody eventType sequence d
            return { Sequence = sequence; Investigation = investigation; CommandId = commandId; Actor = actor; At = occurred; Body = body }
        }
        |> Result.mapError (fun e -> at + ": " + e)

    /// Sequences must be contiguous and strictly increasing (§15: gaps and duplicates are detectable).
    let private checkOrder (events: Event list) : Result<Event list, string> =
        events
        |> List.pairwise
        |> List.tryFind (fun (a, b) -> b.Sequence <> a.Sequence + 1L)
        |> function
            | None ->
                match events with
                | first :: _ when first.Sequence < 1L -> Error "event sequences must start at 1 or later"
                | _ -> Ok events
            | Some(a, b) -> Error("event sequence gap or duplicate: " + string a.Sequence + " is followed by " + string b.Sequence)

    let decode (json: string) : Result<InvestigationId * Event list, string> =
        try
            result {
                let! root = parse json |> Result.bind (asObject "the document")
                let! s = getString root "schema"
                do! if s = schema then Ok() else Error("unsupported schema '" + s + "' (expected '" + schema + "')")
                let! investigation = getId<InvestigationTag> root "investigation"
                let! items = field root "events" |> Result.bind (asArray "events")
                let! events = items |> List.ofSeq |> List.indexed |> traverse (fun (i, n) -> decodeEvent investigation i n)
                let! ordered = checkOrder events
                return investigation, ordered
            }
        with e -> Error("unreadable event document: " + e.Message)

    /// "sha256:<hex>" over the UTF-8 bytes of an encoded document.
    let digest (encoded: string) : string =
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes encoded))

/// The local list of investigations (index document).
[<RequireQualifiedAccess>]
module IndexCodec =
    open Json

    type Entry =
        { Id: InvestigationId
          Title: string
          Lifecycle: string
          Version: int64
          UpdatedAt: DateTimeOffset }

    let schema = "rci.index/v1"

    let encode (entries: Entry list) : string =
        let entry (e: Entry) =
            obj [ "id", id e.Id; "title", str e.Title; "lifecycle", str e.Lifecycle; "version", i64 e.Version; "updatedAt", date e.UpdatedAt ]
        obj [ "schema", str schema; "investigations", arr (entries |> List.map entry) ] |> render

    let private decodeEntry (n: JsonNode | null) : Result<Entry, string> =
        result {
            let! o = asObject "index entry" n
            let! iid = getId<InvestigationTag> o "id"
            let! title = getString o "title"
            let! lifecycle = getString o "lifecycle"
            let! version = getInt64 o "version"
            let! updated = getDate o "updatedAt"
            return { Id = iid; Title = title; Lifecycle = lifecycle; Version = version; UpdatedAt = updated }
        }

    let decode (json: string) : Result<Entry list, string> =
        try
            result {
                let! root = parse json |> Result.bind (asObject "the index")
                let! s = getString root "schema"
                do! if s = schema then Ok() else Error("unsupported schema '" + s + "' (expected '" + schema + "')")
                let! items = field root "investigations" |> Result.bind (asArray "investigations")
                return! items |> List.ofSeq |> traverse decodeEntry
            }
        with e -> Error("unreadable index document: " + e.Message)
