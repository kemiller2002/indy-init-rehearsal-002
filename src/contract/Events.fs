namespace Causa.Contract

open System
open IndyInitRootCauseInvestigator.Engine

/// Field-level encoding/decoding helpers shared by the event codec and the projection.
module internal Fields =
    let unrepresentable detail : Result<'T, CausaError> = CausaError.fail Codes.unrepresentable detail
    let violation detail : Result<'T, CausaError> = CausaError.fail Codes.schemaViolation detail

    let str (s: string) = JStr s
    let uuid (g: Guid) = JStr(Uuid.text g)
    let id (i: Id<'Tag>) = uuid i.Value
    let text (t: NonEmptyText) = JStr t.Value
    let optText (t: NonEmptyText option) = t |> Option.map text |> Option.defaultValue JNull
    let enum (e: WireEnum<'T>) (v: 'T) = JStr(e.ToWire v)

    let ts (what: string) (d: DateTimeOffset) : Result<JValue, CausaError> =
        match Timestamp.tryFormat d with
        | Some s -> Ok(JStr s)
        | None -> unrepresentable (what + " has sub-millisecond precision")

    let optTs what (d: DateTimeOffset option) =
        match d with
        | None -> Ok JNull
        | Some x -> ts what x

    /// Ordered, duplicate-free identifier arrays (CONTRACT §2.7).
    let ids (what: string) (xs: Id<'Tag> list) : Result<JValue, CausaError> =
        let values = xs |> List.map (fun i -> i.Value)
        if (values |> List.distinct |> List.length) <> values.Length then unrepresentable (what + " contains a duplicate identifier")
        else Ok(JArr(values |> List.map uuid))

    let nonBlank (what: string) (s: string) : Result<JValue, CausaError> =
        if String.IsNullOrWhiteSpace s then unrepresentable (what + " must not be blank") else Ok(JStr s)

    // ---- decoding over an object's properties ----
    let get (o: (string * JValue) list) (k: string) : Result<JValue, CausaError> =
        match Json.field k o with
        | Some v -> Ok v
        | None -> violation ("missing field '" + k + "'")

    let getStr o k =
        get o k |> Result.bind (function JStr s -> Ok s | _ -> violation ("field '" + k + "' must be a string"))

    let getInt o k =
        get o k |> Result.bind (function JInt n -> Ok n | _ -> violation ("field '" + k + "' must be an integer"))

    let getObj o k =
        get o k |> Result.bind (function JObj p -> Ok p | _ -> violation ("field '" + k + "' must be an object"))

    let getArr o k =
        get o k |> Result.bind (function JArr a -> Ok a | _ -> violation ("field '" + k + "' must be an array"))

    let toText k (s: string) =
        NonEmptyText.create s |> Result.mapError (fun _ -> CausaError.create Codes.schemaViolation ("field '" + k + "' must contain a non-whitespace character"))

    let getText o k = getStr o k |> Result.bind (toText k)

    let getOptText o k =
        get o k
        |> Result.bind (function
            | JNull -> Ok None
            | JStr s -> toText k s |> Result.map Some
            | _ -> violation ("field '" + k + "' must be a string or null"))

    let getOptStr o k =
        get o k
        |> Result.bind (function
            | JNull -> Ok None
            | JStr s -> Ok(Some s)
            | _ -> violation ("field '" + k + "' must be a string or null"))

    let parseTs k (s: string) =
        match Timestamp.tryParse s with
        | Some d -> Ok d
        | None -> violation ("field '" + k + "' must be a UTC millisecond timestamp")

    let getTs o k = getStr o k |> Result.bind (parseTs k)

    let getOptTs o k =
        get o k
        |> Result.bind (function
            | JNull -> Ok None
            | JStr s -> parseTs k s |> Result.map Some
            | _ -> violation ("field '" + k + "' must be a timestamp or null"))

    let parseUuid k (s: string) =
        match Uuid.tryParse s with
        | Some g -> Ok g
        | None -> violation ("field '" + k + "' must be a canonical lowercase non-nil UUID")

    let getUuid o k = getStr o k |> Result.bind (parseUuid k)
    let getId<'Tag> o k : Result<Id<'Tag>, CausaError> = getUuid o k |> Result.map Id.create<'Tag>

    let getIds<'Tag> o k : Result<Id<'Tag> list, CausaError> =
        getArr o k
        |> Result.bind (traverse (function JStr s -> parseUuid k s |> Result.map Id.create<'Tag> | _ -> violation ("field '" + k + "' must contain UUID strings")))
        |> Result.bind (fun xs ->
            if (xs |> List.distinct |> List.length) <> xs.Length then violation ("field '" + k + "' contains a duplicate identifier") else Ok xs)

    let getEnum (e: WireEnum<'T>) o k : Result<'T, CausaError> =
        getStr o k
        |> Result.bind (fun s ->
            match WireEnum.tryParse e s with
            | Some v -> Ok v
            | None -> violation ("field '" + k + "' has value '" + s + "' outside the " + e.Name + " enumeration"))

    /// The object must have exactly these keys (closed core structure, CONTRACT §2.2).
    let exactKeys (what: string) (keys: string list) (o: (string * JValue) list) : Result<unit, CausaError> =
        let present = o |> List.map fst |> Set.ofList
        let wanted = Set.ofList keys
        let extra = Set.difference present wanted
        let missing = Set.difference wanted present
        if not extra.IsEmpty then violation (what + " has unexpected field(s): " + String.Join(", ", extra))
        elif not missing.IsEmpty then violation (what + " is missing field(s): " + String.Join(", ", missing))
        else Ok()

/// The authoritative event codec (CONTRACT §5, §7): engine Event <-> wire envelope.
[<RequireQualifiedAccess>]
module EventWire =
    open Fields

    /// Registered event discriminators, in engine EventBody declaration order (CONTRACT §7).
    let eventTypes =
        [ "InvestigationStarted"; "LifecycleChanged"; "ObservationRecorded"; "HypothesisRecorded"; "HypothesisTransitioned"
          "EvidenceAdmitted"; "EvidenceSuperseded"; "EvidenceVerificationAssessed"; "EvidenceRelated"; "UnknownIdentified"
          "UnknownTransitioned"; "AssumptionRecorded"; "AssumptionTransitioned"; "ContradictionRecorded"; "ContradictionTransitioned"
          "PredictionRecorded"; "ActionRecorded"; "ActionTransitioned"; "FindingRecorded"; "NegativeKnowledgeEstablished"
          "CausalAssertionRecorded"; "CausalAssertionConfirmed"; "CausalAssertionReopened"; "CorrectiveActionRecorded"
          "CorrectiveActionTransitioned"; "CriterionRecorded"; "VerificationRecorded"; "ConclusionRecorded"; "ProposalRecorded"
          "ProposalWasAccepted"; "ProposalWasRejected" ]

    let envelopeKeys =
        [ "formatVersion"; "eventId"; "investigationId"; "aggregateVersion"; "eventType"; "occurredAt"; "actor"; "semanticVersion"
          "correlationId"; "payload" ]

    let encodeActor (a: Actor) : Result<JValue, CausaError> =
        result {
            let! actorId = nonBlank "actor id" a.Id
            match a.Kind with
            | Human -> return JObj [ "actorId", actorId; "kind", JStr "human" ]
            | System -> return JObj [ "actorId", actorId; "kind", JStr "system" ]
            | Agent(provider, model) ->
                let! p = nonBlank "agent provider" provider
                let! m = nonBlank "agent model" model
                return JObj [ "actorId", actorId; "kind", JStr "agent"; "provider", p; "model", m ]
        }

    let decodeActor (o: (string * JValue) list) : Result<Actor, CausaError> =
        result {
            let! actorId = getStr o "actorId"
            let! kind = getStr o "kind"
            match kind with
            | "human" ->
                do! exactKeys "actor" [ "actorId"; "kind" ] o
                return { Id = actorId; Kind = Human }
            | "system" ->
                do! exactKeys "actor" [ "actorId"; "kind" ] o
                return { Id = actorId; Kind = System }
            | "agent" ->
                do! exactKeys "actor" [ "actorId"; "kind"; "provider"; "model" ] o
                let! provider = getStr o "provider"
                let! model = getStr o "model"
                return { Id = actorId; Kind = Agent(provider, model) }
            | other -> return! violation ("actor kind '" + other + "' is not registered")
        }

    let private lifecycleCommandToWire (engine: string) =
        Enums.lifecycleCommands |> List.tryFind (fst >> (=) engine) |> Option.map snd

    let private lifecycleCommandFromWire (wire: string) =
        Enums.lifecycleCommands |> List.tryFind (snd >> (=) wire) |> Option.map fst

    let private hypothesisCommandFromEngine (s: string) =
        Enums.hypothesisCommand.All |> List.tryFind (fun c -> Enums.hypothesisCommandEngineName c = s)

    /// Payload fields for one event body. Redundant engine copies of envelope data
    /// (HypothesisChange.At / Sequence) are not persisted (CONTRACT §7.2).
    let encodePayload (e: Event) : Result<(string * JValue) list, CausaError> =
        match e.Body with
        | InvestigationStarted(title, oid, original) -> Ok [ "title", text title; "observationId", id oid; "original", text original ]
        | LifecycleChanged(from, target, command, reason) ->
            result {
                let! c =
                    match lifecycleCommandToWire command with
                    | Some c -> Ok c
                    | None -> unrepresentable ("lifecycle command '" + command + "' is not registered")
                let! r =
                    match reason with
                    | None -> Ok JNull
                    | Some s -> nonBlank "lifecycle reason" s
                return [ "from", enum Enums.lifecycle from; "to", enum Enums.lifecycle target; "command", JStr c; "reason", r ]
            }
        | ObservationRecorded(oid, original, at) ->
            optTs "observedAt" at |> Result.map (fun t -> [ "observationId", id oid; "original", text original; "observedAt", t ])
        | HypothesisRecorded(hid, statement, mechanism) ->
            Ok [ "hypothesisId", id hid; "statement", text statement; "mechanism", optText mechanism ]
        | HypothesisTransitioned(hid, c) ->
            result {
                let! command =
                    match hypothesisCommandFromEngine c.Command with
                    | Some cmd -> Ok(enum Enums.hypothesisCommand cmd)
                    | None -> unrepresentable ("hypothesis command '" + c.Command + "' is not registered")
                do! if c.At = e.At then Ok() else unrepresentable "hypothesis change time differs from the event time"
                do! if c.Sequence = e.Sequence then Ok() else unrepresentable "hypothesis change sequence differs from the aggregate version"
                let! evidence = ids "evidenceIds" c.Evidence
                return
                    [ "hypothesisId", id hid; "from", enum Enums.hypothesisState c.From; "to", enum Enums.hypothesisState c.To
                      "command", command; "reason", text c.Reason; "evidenceIds", evidence ]
            }
        | EvidenceAdmitted(eid, content, category, source, captured) ->
            result {
                let! t = optTs "capturedAt" captured
                let kind, body =
                    match content with
                    | TextEvidence x -> "text", x.Value
                    | ArtifactEvidence uri -> "artifact", uri
                let! b = nonBlank "evidence content" body
                return
                    [ "evidenceId", id eid; "contentKind", JStr kind; "content", b; "category", enum Enums.evidenceCategory category
                      "source", text source; "capturedAt", t ]
            }
        | EvidenceSuperseded(old, replacement, reason) ->
            Ok [ "evidenceId", id old; "replacementEvidenceId", id replacement; "reason", text reason ]
        | EvidenceVerificationAssessed(eid, status) -> Ok [ "evidenceId", id eid; "verification", enum Enums.verificationStatus status ]
        | EvidenceRelated(rid, eid, hid, kind, rationale) ->
            Ok [ "relationshipId", id rid; "evidenceId", id eid; "hypothesisId", id hid; "kind", enum Enums.relationshipKind kind
                 "rationale", optText rationale ]
        | UnknownIdentified(uid, question, m) -> Ok [ "unknownId", id uid; "question", text question; "materiality", enum Enums.materiality m ]
        | UnknownTransitioned(uid, state, basis) -> Ok [ "unknownId", id uid; "state", enum Enums.unknownState state; "basis", text basis ]
        | AssumptionRecorded(aid, prop, m) -> Ok [ "assumptionId", id aid; "proposition", text prop; "materiality", enum Enums.materiality m ]
        | AssumptionTransitioned(aid, state) -> Ok [ "assumptionId", id aid; "state", enum Enums.assumptionState state ]
        | ContradictionRecorded(cid, statement, evidence, m) ->
            ids "evidenceIds" evidence
            |> Result.map (fun ev -> [ "contradictionId", id cid; "statement", text statement; "evidenceIds", ev; "materiality", enum Enums.materiality m ])
        | ContradictionTransitioned(cid, state, basis) ->
            Ok [ "contradictionId", id cid; "state", enum Enums.contradictionState state; "basis", text basis ]
        | PredictionRecorded(pid, hid, statement, strength, timing) ->
            Ok [ "predictionId", id pid; "hypothesisId", id hid; "statement", text statement
                 "strength", enum Enums.predictionStrength strength; "timing", enum Enums.predictionTiming timing ]
        | ActionRecorded(aid, kind, purpose, targets) ->
            ids "targetHypothesisIds" targets
            |> Result.map (fun t -> [ "actionId", id aid; "kind", enum Enums.actionKind kind; "purpose", text purpose; "targetHypothesisIds", t ])
        | ActionTransitioned(aid, state) -> Ok [ "actionId", id aid; "state", enum Enums.actionState state ]
        | FindingRecorded(fid, aid, r, interpretation) ->
            Ok [ "findingId", id fid; "actionId", id aid; "originalResult", text r; "interpretation", optText interpretation ]
        | NegativeKnowledgeEstablished(nid, statement, scope, basis) ->
            ids "basisEvidenceIds" basis
            |> Result.map (fun b -> [ "negativeKnowledgeId", id nid; "statement", text statement; "scope", text scope; "basisEvidenceIds", b ])
        | CausalAssertionRecorded(cid, outcome, members, mechanism) ->
            // Matches the schema's whole-member uniqueItems: an identical (hypothesis, role) pair twice has no wire form.
            if (members |> List.distinct |> List.length) <> members.Length then unrepresentable "causal members repeat an identical member"
            else
                let ms = members |> List.map (fun m -> JObj [ "hypothesisId", id m.Hypothesis; "role", enum Enums.causeRole m.Role ])
                Ok [ "causeId", id cid; "outcome", text outcome; "members", JArr ms; "mechanism", optText mechanism ]
        | CausalAssertionConfirmed(cid, policy) -> nonBlank "policy" policy |> Result.map (fun p -> [ "causeId", id cid; "policy", p ])
        | CausalAssertionReopened(cid, reason) -> Ok [ "causeId", id cid; "reason", text reason ]
        | CorrectiveActionRecorded(caid, kind, description, targets) ->
            ids "targetCauseIds" targets
            |> Result.map (fun t ->
                [ "correctiveActionId", id caid; "kind", enum Enums.correctiveActionKind kind; "description", text description; "targetCauseIds", t ])
        | CorrectiveActionTransitioned(caid, state) -> Ok [ "correctiveActionId", id caid; "state", enum Enums.correctiveActionState state ]
        | CriterionRecorded(crid, caid, expected, method) ->
            Ok [ "criterionId", id crid; "correctiveActionId", id caid; "expectedOutcome", text expected; "method", optText method ]
        | VerificationRecorded(vid, crid, outcome, evidence) ->
            ids "evidenceIds" evidence
            |> Result.map (fun ev -> [ "verificationId", id vid; "criterionId", id crid; "outcome", enum Enums.verificationOutcome outcome; "evidenceIds", ev ])
        | ConclusionRecorded(cid, statement, strength, causes, residual) ->
            ids "causeIds" causes
            |> Result.map (fun c ->
                [ "conclusionId", id cid; "statement", text statement; "strength", enum Enums.conclusionStrength strength; "causeIds", c
                  "residualUncertainty", optText residual ])
        | ProposalRecorded(pid, kind, t, rationale) ->
            Ok [ "proposalId", id pid; "kind", enum Enums.proposalKind kind; "text", text t; "rationale", optText rationale ]
        | ProposalWasAccepted(pid, target) ->
            if target = Guid.Empty then unrepresentable "accepted proposal target is the nil UUID"
            else Ok [ "proposalId", id pid; "targetId", uuid target ]
        | ProposalWasRejected(pid, reason) -> Ok [ "proposalId", id pid; "reason", text reason ]

    let rec private containsNil (v: JValue) =
        match v with
        | JStr "00000000-0000-0000-0000-000000000000" -> true
        | JArr xs -> xs |> List.exists containsNil
        | JObj o -> o |> List.exists (snd >> containsNil)
        | _ -> false

    let encode (e: Event) : Result<JValue, CausaError> =
        result {
            let! at = ts "occurredAt" e.At
            let! actor = encodeActor e.Actor
            let! payload = encodePayload e
            do! if e.CommandId = Guid.Empty then unrepresentable "command id is the nil UUID" else Ok()
            do! if e.Investigation.Value = Guid.Empty || containsNil (JObj payload) then unrepresentable "an identifier is the nil UUID" else Ok()
            return
                JObj
                    [ "formatVersion", JStr Contract.formatVersion
                      "eventId", uuid (Uuid.eventId e.Investigation.Value e.Sequence e.CommandId)
                      "investigationId", id e.Investigation
                      "aggregateVersion", JInt e.Sequence
                      "eventType", JStr(CaseNames.eventType e.Body)
                      "occurredAt", at
                      "actor", actor
                      "semanticVersion", JStr Contract.semanticVersion
                      "correlationId", uuid e.CommandId
                      "payload", JObj payload ]
        }

    let private body (eventType: string) (version: int64) (at: DateTimeOffset) (d: (string * JValue) list) : Result<EventBody, CausaError> =
        let keys = exactKeys ("payload of " + eventType)
        match eventType with
        | "InvestigationStarted" ->
            result {
                do! keys [ "title"; "observationId"; "original" ] d
                let! title = getText d "title"
                let! oid = getId d "observationId"
                let! original = getText d "original"
                return InvestigationStarted(title, oid, original)
            }
        | "LifecycleChanged" ->
            result {
                do! keys [ "from"; "to"; "command"; "reason" ] d
                let! from = getEnum Enums.lifecycle d "from"
                let! target = getEnum Enums.lifecycle d "to"
                let! wire = getStr d "command"
                let! command =
                    match lifecycleCommandFromWire wire with
                    | Some c -> Ok c
                    | None -> violation ("lifecycle command '" + wire + "' is not registered")
                let! reason = getOptStr d "reason"
                do! match reason with
                    | Some s when String.IsNullOrWhiteSpace s -> violation "lifecycle reason must not be blank"
                    | _ -> Ok()
                return LifecycleChanged(from, target, command, reason)
            }
        | "ObservationRecorded" ->
            result {
                do! keys [ "observationId"; "original"; "observedAt" ] d
                let! oid = getId d "observationId"
                let! original = getText d "original"
                let! observed = getOptTs d "observedAt"
                return ObservationRecorded(oid, original, observed)
            }
        | "HypothesisRecorded" ->
            result {
                do! keys [ "hypothesisId"; "statement"; "mechanism" ] d
                let! hid = getId d "hypothesisId"
                let! statement = getText d "statement"
                let! mechanism = getOptText d "mechanism"
                return HypothesisRecorded(hid, statement, mechanism)
            }
        | "HypothesisTransitioned" ->
            result {
                do! keys [ "hypothesisId"; "from"; "to"; "command"; "reason"; "evidenceIds" ] d
                let! hid = getId d "hypothesisId"
                let! from = getEnum Enums.hypothesisState d "from"
                let! target = getEnum Enums.hypothesisState d "to"
                let! command = getEnum Enums.hypothesisCommand d "command"
                let! reason = getText d "reason"
                let! evidence = getIds d "evidenceIds"
                return
                    HypothesisTransitioned(
                        hid,
                        { From = from; To = target; Command = Enums.hypothesisCommandEngineName command; Reason = reason
                          Evidence = evidence; At = at; Sequence = version }
                    )
            }
        | "EvidenceAdmitted" ->
            result {
                do! keys [ "evidenceId"; "contentKind"; "content"; "category"; "source"; "capturedAt" ] d
                let! eid = getId d "evidenceId"
                let! kind = getStr d "contentKind"
                let! raw = getStr d "content"
                let! content =
                    match kind with
                    | "text" -> toText "content" raw |> Result.map TextEvidence
                    | "artifact" -> if String.IsNullOrWhiteSpace raw then violation "artifact content must not be blank" else Ok(ArtifactEvidence raw)
                    | other -> violation ("contentKind '" + other + "' is not registered")
                let! category = getEnum Enums.evidenceCategory d "category"
                let! source = getText d "source"
                let! captured = getOptTs d "capturedAt"
                return EvidenceAdmitted(eid, content, category, source, captured)
            }
        | "EvidenceSuperseded" ->
            result {
                do! keys [ "evidenceId"; "replacementEvidenceId"; "reason" ] d
                let! old = getId d "evidenceId"
                let! replacement = getId d "replacementEvidenceId"
                let! reason = getText d "reason"
                return EvidenceSuperseded(old, replacement, reason)
            }
        | "EvidenceVerificationAssessed" ->
            result {
                do! keys [ "evidenceId"; "verification" ] d
                let! eid = getId d "evidenceId"
                let! status = getEnum Enums.verificationStatus d "verification"
                return EvidenceVerificationAssessed(eid, status)
            }
        | "EvidenceRelated" ->
            result {
                do! keys [ "relationshipId"; "evidenceId"; "hypothesisId"; "kind"; "rationale" ] d
                let! rid = getId d "relationshipId"
                let! eid = getId d "evidenceId"
                let! hid = getId d "hypothesisId"
                let! kind = getEnum Enums.relationshipKind d "kind"
                let! rationale = getOptText d "rationale"
                return EvidenceRelated(rid, eid, hid, kind, rationale)
            }
        | "UnknownIdentified" ->
            result {
                do! keys [ "unknownId"; "question"; "materiality" ] d
                let! uid = getId d "unknownId"
                let! question = getText d "question"
                let! m = getEnum Enums.materiality d "materiality"
                return UnknownIdentified(uid, question, m)
            }
        | "UnknownTransitioned" ->
            result {
                do! keys [ "unknownId"; "state"; "basis" ] d
                let! uid = getId d "unknownId"
                let! state = getEnum Enums.unknownState d "state"
                let! basis = getText d "basis"
                return UnknownTransitioned(uid, state, basis)
            }
        | "AssumptionRecorded" ->
            result {
                do! keys [ "assumptionId"; "proposition"; "materiality" ] d
                let! aid = getId d "assumptionId"
                let! prop = getText d "proposition"
                let! m = getEnum Enums.materiality d "materiality"
                return AssumptionRecorded(aid, prop, m)
            }
        | "AssumptionTransitioned" ->
            result {
                do! keys [ "assumptionId"; "state" ] d
                let! aid = getId d "assumptionId"
                let! state = getEnum Enums.assumptionState d "state"
                return AssumptionTransitioned(aid, state)
            }
        | "ContradictionRecorded" ->
            result {
                do! keys [ "contradictionId"; "statement"; "evidenceIds"; "materiality" ] d
                let! cid = getId d "contradictionId"
                let! statement = getText d "statement"
                let! evidence = getIds d "evidenceIds"
                let! m = getEnum Enums.materiality d "materiality"
                return ContradictionRecorded(cid, statement, evidence, m)
            }
        | "ContradictionTransitioned" ->
            result {
                do! keys [ "contradictionId"; "state"; "basis" ] d
                let! cid = getId d "contradictionId"
                let! state = getEnum Enums.contradictionState d "state"
                let! basis = getText d "basis"
                return ContradictionTransitioned(cid, state, basis)
            }
        | "PredictionRecorded" ->
            result {
                do! keys [ "predictionId"; "hypothesisId"; "statement"; "strength"; "timing" ] d
                let! pid = getId d "predictionId"
                let! hid = getId d "hypothesisId"
                let! statement = getText d "statement"
                let! strength = getEnum Enums.predictionStrength d "strength"
                let! timing = getEnum Enums.predictionTiming d "timing"
                return PredictionRecorded(pid, hid, statement, strength, timing)
            }
        | "ActionRecorded" ->
            result {
                do! keys [ "actionId"; "kind"; "purpose"; "targetHypothesisIds" ] d
                let! aid = getId d "actionId"
                let! kind = getEnum Enums.actionKind d "kind"
                let! purpose = getText d "purpose"
                let! targets = getIds d "targetHypothesisIds"
                return ActionRecorded(aid, kind, purpose, targets)
            }
        | "ActionTransitioned" ->
            result {
                do! keys [ "actionId"; "state" ] d
                let! aid = getId d "actionId"
                let! state = getEnum Enums.actionState d "state"
                return ActionTransitioned(aid, state)
            }
        | "FindingRecorded" ->
            result {
                do! keys [ "findingId"; "actionId"; "originalResult"; "interpretation" ] d
                let! fid = getId d "findingId"
                let! aid = getId d "actionId"
                let! r = getText d "originalResult"
                let! interpretation = getOptText d "interpretation"
                return FindingRecorded(fid, aid, r, interpretation)
            }
        | "NegativeKnowledgeEstablished" ->
            result {
                do! keys [ "negativeKnowledgeId"; "statement"; "scope"; "basisEvidenceIds" ] d
                let! nid = getId d "negativeKnowledgeId"
                let! statement = getText d "statement"
                let! scope = getText d "scope"
                let! basis = getIds d "basisEvidenceIds"
                return NegativeKnowledgeEstablished(nid, statement, scope, basis)
            }
        | "CausalAssertionRecorded" ->
            result {
                do! keys [ "causeId"; "outcome"; "members"; "mechanism" ] d
                let! cid = getId d "causeId"
                let! outcome = getText d "outcome"
                let! raw = getArr d "members"
                let! members =
                    raw
                    |> traverse (function
                        | JObj m ->
                            result {
                                do! exactKeys "causal member" [ "hypothesisId"; "role" ] m
                                let! h = getId m "hypothesisId"
                                let! role = getEnum Enums.causeRole m "role"
                                return { Hypothesis = h; Role = role }
                            }
                        | _ -> violation "causal members must be objects")
                let! mechanism = getOptText d "mechanism"
                return CausalAssertionRecorded(cid, outcome, members, mechanism)
            }
        | "CausalAssertionConfirmed" ->
            result {
                do! keys [ "causeId"; "policy" ] d
                let! cid = getId d "causeId"
                let! policy = getStr d "policy"
                return CausalAssertionConfirmed(cid, policy)
            }
        | "CausalAssertionReopened" ->
            result {
                do! keys [ "causeId"; "reason" ] d
                let! cid = getId d "causeId"
                let! reason = getText d "reason"
                return CausalAssertionReopened(cid, reason)
            }
        | "CorrectiveActionRecorded" ->
            result {
                do! keys [ "correctiveActionId"; "kind"; "description"; "targetCauseIds" ] d
                let! caid = getId d "correctiveActionId"
                let! kind = getEnum Enums.correctiveActionKind d "kind"
                let! description = getText d "description"
                let! targets = getIds d "targetCauseIds"
                return CorrectiveActionRecorded(caid, kind, description, targets)
            }
        | "CorrectiveActionTransitioned" ->
            result {
                do! keys [ "correctiveActionId"; "state" ] d
                let! caid = getId d "correctiveActionId"
                let! state = getEnum Enums.correctiveActionState d "state"
                return CorrectiveActionTransitioned(caid, state)
            }
        | "CriterionRecorded" ->
            result {
                do! keys [ "criterionId"; "correctiveActionId"; "expectedOutcome"; "method" ] d
                let! crid = getId d "criterionId"
                let! caid = getId d "correctiveActionId"
                let! expected = getText d "expectedOutcome"
                let! method = getOptText d "method"
                return CriterionRecorded(crid, caid, expected, method)
            }
        | "VerificationRecorded" ->
            result {
                do! keys [ "verificationId"; "criterionId"; "outcome"; "evidenceIds" ] d
                let! vid = getId d "verificationId"
                let! crid = getId d "criterionId"
                let! outcome = getEnum Enums.verificationOutcome d "outcome"
                let! evidence = getIds d "evidenceIds"
                return VerificationRecorded(vid, crid, outcome, evidence)
            }
        | "ConclusionRecorded" ->
            result {
                do! keys [ "conclusionId"; "statement"; "strength"; "causeIds"; "residualUncertainty" ] d
                let! cid = getId d "conclusionId"
                let! statement = getText d "statement"
                let! strength = getEnum Enums.conclusionStrength d "strength"
                let! causes = getIds d "causeIds"
                let! residual = getOptText d "residualUncertainty"
                return ConclusionRecorded(cid, statement, strength, causes, residual)
            }
        | "ProposalRecorded" ->
            result {
                do! keys [ "proposalId"; "kind"; "text"; "rationale" ] d
                let! pid = getId d "proposalId"
                let! kind = getEnum Enums.proposalKind d "kind"
                let! t = getText d "text"
                let! rationale = getOptText d "rationale"
                return ProposalRecorded(pid, kind, t, rationale)
            }
        | "ProposalWasAccepted" ->
            result {
                do! keys [ "proposalId"; "targetId" ] d
                let! pid = getId d "proposalId"
                let! target = getUuid d "targetId"
                return ProposalWasAccepted(pid, target)
            }
        | "ProposalWasRejected" ->
            result {
                do! keys [ "proposalId"; "reason" ] d
                let! pid = getId d "proposalId"
                let! reason = getText d "reason"
                return ProposalWasRejected(pid, reason)
            }
        // CONTRACT §7.1: an unregistered discriminator fails closed; it is never skipped.
        | other -> CausaError.fail Codes.unknownEventType ("event type '" + other + "' is not registered in v1.0.0")

    /// Decoded envelope plus the wire-only identity fields the engine does not keep.
    type Decoded =
        { Event: Event
          EventId: Guid
          InvestigationId: Guid }

    let decode (v: JValue) : Result<Decoded, CausaError> =
        match v with
        | JObj o ->
            result {
                do! exactKeys "event envelope" envelopeKeys o
                let! fv = getStr o "formatVersion"
                do! if fv = Contract.formatVersion then Ok() else CausaError.fail Codes.unsupportedVersion ("formatVersion '" + fv + "'")
                let! sv = getStr o "semanticVersion"
                do! if sv = Contract.semanticVersion then Ok() else CausaError.fail Codes.unsupportedVersion ("semanticVersion '" + sv + "'")
                let! eventId = getUuid o "eventId"
                let! investigation = getUuid o "investigationId"
                let! version = getInt o "aggregateVersion"
                do! if version >= 1L then Ok() else violation "aggregateVersion must be >= 1"
                let! eventType = getStr o "eventType"
                let! at = getTs o "occurredAt"
                let! actor = getObj o "actor" |> Result.bind decodeActor
                let! correlation = getUuid o "correlationId"
                let! payload = getObj o "payload"
                let! b = body eventType version at payload
                return
                    { Event =
                        { Sequence = version; Investigation = Id.create investigation; CommandId = correlation; Actor = actor; At = at; Body = b }
                      EventId = eventId
                      InvestigationId = investigation }
            }
        | _ -> violation "an event must be a JSON object"
