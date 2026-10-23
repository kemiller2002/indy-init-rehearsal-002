namespace IndyInitRootCauseInvestigator.Engine

open System

/// Structural transition tables (STATE-TRANSITION-TABLES.md). These are the
/// only copies; decide, capabilities and tests all read them (DEC-04).
[<RequireQualifiedAccess>]
module Transitions =
    let hypothesis (state: HypothesisState) (command: HypothesisCommand) : HypothesisState option =
        match state, command with
        | Proposed, MarkPlausible -> Some Plausible
        | Plausible, MarkSupported -> Some Supported
        | (Plausible | Supported), MarkWeakened -> Some Weakened
        | (Proposed | Plausible | Supported | Weakened), Falsify -> Some Falsified
        | (Proposed | Plausible | Supported | Weakened), Supersede -> Some Superseded
        | Falsified, Reopen -> Some Plausible
        | Weakened, MarkPlausible -> Some Plausible
        | Weakened, MarkSupported -> Some Supported
        | _ -> None

    /// Commands whose basis must cite evidence (R2.3, R2.5).
    let requiresEvidence (command: HypothesisCommand) =
        match command with
        | MarkSupported | MarkWeakened | Falsify | Reopen -> true
        | MarkPlausible | Supersede -> false

    let unknown (state: UnknownState) (command: UnknownCommand) =
        match state, command with
        | Identified, StartInvestigatingUnknown -> Some Investigating
        | (Identified | Investigating), ResolveUnknown -> Some UnknownResolved
        | (Identified | Investigating), AcceptUnknownAsResidual -> Some AcceptedResidual
        | _ -> None

    let contradiction (state: ContradictionState) (command: ContradictionCommand) =
        match state, command with
        | Detected, TriageContradiction -> Some Triaged
        | (Detected | Triaged | ContradictionInvestigating), ResolveContradiction -> Some ContradictionResolved
        | (Detected | Triaged | ContradictionInvestigating), AcceptContradictionAsResidual -> Some ContradictionAcceptedResidual
        | _ -> None

    let assumption (state: AssumptionState) (command: AssumptionCommand) =
        match state, command with
        | ExplicitUnverified, ProvisionallyAcceptAssumption -> Some ProvisionallyAccepted
        | (ExplicitUnverified | ProvisionallyAccepted), VerifyAssumption -> Some AssumptionVerified
        | (ExplicitUnverified | ProvisionallyAccepted), RejectAssumption -> Some Rejected
        | _ -> None

    let action (state: ActionState) (command: ActionCommand) =
        match state, command with
        | ActionProposed, ApproveAction -> Some ActionApproved
        | ActionProposed, RejectAction -> Some ActionRejected
        | ActionApproved, StartAction -> Some ActionInProgress
        | (ActionApproved | ActionInProgress), CancelAction -> Some ActionCancelled
        | _ -> None

    let correction (state: CorrectiveActionState) (command: CorrectionCommand) =
        match state, command with
        | CorrectionProposed, ApproveCorrection -> Some CorrectionApproved
        | (CorrectionProposed | CorrectionApproved), CancelCorrection -> Some CorrectionCancelled
        | (CorrectionApproved | ReworkRequired), StartCorrection -> Some CorrectionInProgress
        | CorrectionInProgress, MarkImplemented -> Some Implemented
        | Implemented, StartVerificationOfEffect -> Some AwaitingVerificationOfEffect
        | VerificationFailed, RequireRework -> Some ReworkRequired
        | _ -> None

    let lifecycle (state: InvestigationLifecycle) (command: Command) =
        match state, command with
        | (Draft | AwaitingEvidence | AwaitingVerification | Archived), ActivateInvestigation -> Some Active
        | Active, MarkAwaitingEvidence _ -> Some AwaitingEvidence
        | Active, MarkAwaitingVerification -> Some AwaitingVerification
        | (Active | AwaitingVerification), ResolveInvestigation -> Some Resolved
        | Resolved, CloseInvestigation -> Some Closed
        | (Resolved | Closed), ReopenInvestigation _ -> Some Active
        | _ -> None

    /// Investigations whose content may change (frozen once resolved, D-005).
    let isOpen (state: InvestigationLifecycle) =
        match state with
        | Draft | Active | AwaitingEvidence | AwaitingVerification -> true
        | Resolved | Closed | Archived -> false

[<RequireQualifiedAccess>]
module Policy =
    let id = "core-default/1"

    let private err code rule message = DomainError.create code rule message

    let private isOpenUnknown (u: Unknown) =
        match u.State with
        | Identified | Investigating -> true
        | _ -> false

    let private isOpenContradiction (c: Contradiction) =
        match c.State with
        | Detected | Triaged | ContradictionInvestigating -> true
        | _ -> false

    let private isMaterial m =
        match m with
        | Blocking | Material -> true
        | NonMaterial | Informational -> false

    let private label (text: NonEmptyText) =
        let s = text.Value
        if s.Length <= 60 then s else s.Substring(0, 57) + "..."

    let private activeEvidence (inv: Investigation) (eid: EvidenceId) =
        inv.Evidence |> Map.tryFind eid |> Option.exists (fun e -> e.SupersededBy.IsNone)

    /// Open blocking unknowns (R5.2: an unknown may create required work).
    let blockingUnknowns (inv: Investigation) =
        inv.Unknowns |> Map.toList |> List.map snd
        |> List.filter (fun u -> isOpenUnknown u && u.Materiality = Blocking)

    /// Open unknowns that are Blocking or Material (R6.4: unresolved uncertainty).
    let unresolvedMaterialUnknowns (inv: Investigation) =
        inv.Unknowns |> Map.toList |> List.map snd
        |> List.filter (fun u -> isOpenUnknown u && isMaterial u.Materiality)

    /// The evidential conditions of the cause gate (§15 items 1,3–8), independent of the
    /// assertion's own state, so resolution can re-check a confirmed cause (C1, I-19/I-20).
    let causeSupportFindings (inv: Investigation) (ca: CausalAssertion) : DomainError list =
        let rootBlock =
            if ca.Members |> List.exists (fun m -> m.Role = RootCause) then []
            else [ err "CAUSE.NO_ROOT" "R6.3" "Name at least one member as the root cause (contributing factors alone are not a root cause)." ]
        let memberBlocks =
            ca.Members |> List.collect (fun m ->
                match inv.Hypotheses |> Map.tryFind m.Hypothesis with
                | None -> [ err "CAUSE.MEMBER_MISSING" "I-19" "A cause member names a hypothesis that does not exist." ]
                | Some h ->
                    let supported =
                        if h.State = Supported then []
                        else [ err "CAUSE.MEMBER_NOT_SUPPORTED" "§15.3" $"Hypothesis \"{label h.Statement}\" is {h.State}, not Supported." ]
                    let evidence =
                        let supporting =
                            inv.Relationships |> Map.exists (fun _ r ->
                                r.Hypothesis = h.Id && r.Kind = Supports && activeEvidence inv r.Evidence)
                        if supporting then []
                        else [ err "CAUSE.NO_SUPPORTING_EVIDENCE" "§15.3" $"No active evidence supports \"{label h.Statement}\"." ]
                    // N3 (§15.4, R3.4): contradicting evidence must be resolved or accepted through a Contradiction record.
                    let addressed (eid: EvidenceId) =
                        inv.Contradictions |> Map.exists (fun _ c ->
                            List.contains eid c.Evidence && (c.State = ContradictionResolved || c.State = ContradictionAcceptedResidual))
                    let contradicting =
                        inv.Relationships |> Map.toList |> List.map snd
                        |> List.filter (fun r -> r.Hypothesis = h.Id && r.Kind = Contradicts && activeEvidence inv r.Evidence && not (addressed r.Evidence))
                        |> List.map (fun _ -> err "CAUSE.CONTRADICTING_EVIDENCE" "§15.4" $"Evidence contradicts \"{label h.Statement}\"; record and resolve (or accept) the contradiction first.")
                    supported @ evidence @ contradicting)
        let contradictionBlocks =
            inv.Contradictions |> Map.toList |> List.map snd
            |> List.filter (fun c -> isOpenContradiction c && isMaterial c.Materiality)
            |> List.map (fun c -> err "CAUSE.OPEN_CONTRADICTION" "§15.4" $"Contradiction \"{label c.Statement}\" is unresolved.")
        let unknownBlocks =
            inv.Unknowns |> Map.toList |> List.map snd
            |> List.filter (fun u -> isOpenUnknown u && isMaterial u.Materiality)
            |> List.map (fun u -> err "CAUSE.OPEN_UNKNOWN" "§15.5" $"Unknown \"{label u.Question}\" is unresolved ({u.Materiality}).")
        let assumptionBlocks =
            inv.Assumptions |> Map.toList |> List.map snd
            |> List.filter (fun a -> a.State = ExplicitUnverified && isMaterial a.Materiality)
            |> List.map (fun a -> err "CAUSE.UNVERIFIED_ASSUMPTION" "§15.6" $"Assumption \"{label a.Proposition}\" is unverified.")
        let members = ca.Members |> List.map (fun m -> m.Hypothesis) |> Set.ofList
        let alternativeBlocks =
            inv.Hypotheses |> Map.toList |> List.map snd
            |> List.filter (fun h -> not (members.Contains h.Id))
            |> List.filter (fun h -> match h.State with Proposed | Plausible | Supported -> true | _ -> false)
            |> List.map (fun h -> err "CAUSE.ALTERNATIVE_NOT_ADDRESSED" "§15.7" $"Alternative hypothesis \"{label h.Statement}\" is still {h.State}; weaken, falsify, supersede or include it.")
        let mechanismBlock =
            match ca.Mechanism with
            | Some _ -> []
            | None -> [ err "CAUSE.NO_MECHANISM" "§15.8" "The causal mechanism is not stated." ]
        rootBlock @ memberBlocks @ contradictionBlocks @ unknownBlocks @ assumptionBlocks @ alternativeBlocks @ mechanismBlock

    /// Cause Confirmation Gate (INVESTIGATION-SEMANTICS §15, POLICY-ENGINE §24).
    /// Returns every material blocker, not only the first.
    let causeBlockers (inv: Investigation) (cid: CausalAssertionId) : DomainError list =
        match inv.Causes |> Map.tryFind cid with
        | None -> [ err "CAUSE.NOT_FOUND" "I-19" "The causal assertion does not exist." ]
        | Some ca ->
            let stateBlock =
                match ca.State with
                | CauseProposed | CauseReopened -> []
                | s -> [ err "CAUSE.STATE" "STATE-TRANSITION-TABLES §10" $"A cause in state {s} cannot be confirmed." ]
            let lifecycleBlock =
                match inv.Lifecycle with
                | Active | AwaitingEvidence | AwaitingVerification -> []
                | s -> [ err "INVESTIGATION.NOT_OPEN" "D-005" $"The investigation is {s}." ]
            stateBlock @ lifecycleBlock @ causeSupportFindings inv ca

    /// True when ConfirmCause would pass the gate now (for UI visibility; never a second rule).
    let canConfirm (inv: Investigation) (cid: CausalAssertionId) = List.isEmpty (causeBlockers inv cid)

    let private criteriaOf (inv: Investigation) (caId: CorrectiveActionId) =
        inv.Criteria |> Map.toList |> List.map snd |> List.filter (fun c -> c.CorrectiveAction = caId)

    /// Sequence at which the corrective action's current verification round began.
    let verificationRound (inv: Investigation) (caId: CorrectiveActionId) =
        inv.CorrectiveActions |> Map.tryFind caId |> Option.map (fun ca -> ca.AwaitingSince) |> Option.defaultValue 0L

    /// Evidence admitted after the round began and still active (N2, M2).
    let freshEvidence (inv: Investigation) (round: int64) (eid: EvidenceId) =
        activeEvidence inv eid && (inv.Evidence |> Map.tryFind eid |> Option.exists (fun ev -> ev.AdmittedAt > round))

    /// THE definition of "criterion met" (UI must use it): a Satisfied verification in the
    /// current round citing only evidence admitted during that round and still active.
    let criterionSatisfied (inv: Investigation) (crit: Criterion) =
        let round = verificationRound inv crit.CorrectiveAction
        inv.Verifications |> Map.exists (fun _ v ->
            v.Criterion = crit.Id && v.Outcome = Satisfied && v.Sequence > round && not v.Evidence.IsEmpty
            && v.Evidence |> List.forall (freshEvidence inv round))

    let private satisfiedWithEvidence inv crit = criterionSatisfied inv crit

    /// True when every criterion of the corrective action has a Satisfied verification citing evidence.
    let allCriteriaSatisfied (inv: Investigation) (caId: CorrectiveActionId) =
        let criteria = criteriaOf inv caId
        not criteria.IsEmpty && criteria |> List.forall (satisfiedWithEvidence inv)

    /// Resolution gate (I-20, POLICY-ENGINE §25, R7.4, A32, A35).
    let resolutionBlockers (inv: Investigation) : DomainError list =
        let confirmed = inv.Causes |> Map.toList |> List.map snd |> List.filter (fun c -> c.State = CauseConfirmed)
        let confirmedIds = confirmed |> List.map (fun c -> c.Id) |> Set.ofList
        let causeBlock =
            if confirmed.IsEmpty then [ err "RESOLVE.NO_CONFIRMED_CAUSE" "R6.1" "No root cause has been confirmed." ] else []
        let corrections =
            inv.CorrectiveActions |> Map.toList |> List.map snd
            |> List.filter (fun ca -> ca.Kind = Correction && ca.Targets |> List.exists confirmedIds.Contains)
        let correctionBlock =
            if corrections.IsEmpty then
                [ err "RESOLVE.NO_CORRECTION" "R7.2" "No root-cause correction targets the confirmed cause (a mitigation alone does not resolve the cause)." ]
            elif corrections |> List.exists (fun ca -> ca.State = VerifiedEffective) then []
            else [ err "RESOLVE.CORRECTION_NOT_VERIFIED" "R7.4" "No correction has verification evidence that its success criteria were satisfied." ]
        let pendingCriteria =
            corrections
            |> List.filter (fun ca -> ca.State <> CorrectionCancelled)
            |> List.collect (fun ca -> criteriaOf inv ca.Id)
            |> List.filter (fun c -> not (satisfiedWithEvidence inv c))
            |> List.map (fun c -> err "RESOLVE.CRITERION_UNSATISFIED" "R7.3" $"Verification criterion \"{label c.ExpectedOutcome}\" has no satisfied verification.")
        let unknownBlocks =
            blockingUnknowns inv
            |> List.map (fun u -> err "RESOLVE.BLOCKING_UNKNOWN" "R5.2" $"Blocking unknown \"{label u.Question}\" is open.")
        let conclusionBlock =
            if inv.Conclusions.IsEmpty then [ err "RESOLVE.NO_CONCLUSION" "R6.4" "Record a conclusion (with residual uncertainty) before resolving." ] else []
        let stillHolds =
            confirmed |> List.collect (fun c ->
                causeSupportFindings inv c
                |> List.map (fun f -> err "RESOLVE.CAUSE_NO_LONGER_HOLDS" f.Rule $"The confirmed cause no longer meets the gate: {f.Message} Reopen and reassess it."))
        // N5/N7: the current conclusion must still be true of the present state.
        let latest = inv.Conclusions |> Map.toList |> List.map snd |> List.sortBy (fun c -> c.Sequence) |> List.tryLast
        let residualsAccepted =
            (inv.Unknowns |> Map.exists (fun _ u -> u.State = AcceptedResidual))
            || (inv.Contradictions |> Map.exists (fun _ c -> c.State = ContradictionAcceptedResidual))
        let conclusionStale =
            match latest with
            | None -> []
            | Some c ->
                let staleCause =
                    if c.Strength = ConfirmedCause && (c.Causes.IsEmpty || c.Causes |> List.exists (fun k -> not (confirmedIds.Contains k))) then
                        [ err "RESOLVE.CONCLUSION_STALE" "R6.1" "The current conclusion names a cause that is no longer confirmed; record a new conclusion." ]
                    else []
                let residual =
                    if residualsAccepted && c.ResidualUncertainty.IsNone then
                        [ err "RESOLVE.RESIDUAL_NOT_STATED" "R6.4" "Uncertainty was accepted as residual; the conclusion must state it." ]
                    else []
                staleCause @ residual
        causeBlock @ stillHolds @ correctionBlock @ pendingCriteria @ unknownBlocks @ conclusionBlock @ conclusionStale

[<RequireQualifiedAccess>]
module Evolve =
    /// Apply a body during decide (used to evaluate follow-on rules on the post-state).
    let rec applyBody (inv: Investigation) (sequence: int64) (env: CommandEnvelope) (body: EventBody) : Investigation =
        apply (Some inv) { Sequence = sequence; Investigation = env.Investigation; CommandId = env.CommandId; Actor = env.Actor; At = env.RequestedAt; Body = body }

    /// Total fold step. Events are already validated; unknown targets are ignored, never thrown.
    and apply (state: Investigation option) (e: Event) : Investigation =
        let p = { Actor = e.Actor; RecordedAt = e.At }
        let upd key f (m: Map<'K, 'V>) = match Map.tryFind key m with Some v -> Map.add key (f v) m | None -> m
        match state, e.Body with
        | None, InvestigationStarted(title, oid, original) ->
            { Id = e.Investigation; Title = title; Lifecycle = Draft; Version = e.Sequence; CreatedAt = e.At
              Observations = Map.ofList [ oid, { Id = oid; Original = original; ObservedAt = None; Provenance = p } ]
              Hypotheses = Map.empty; Evidence = Map.empty; Relationships = Map.empty; Unknowns = Map.empty
              Assumptions = Map.empty; Contradictions = Map.empty; Predictions = Map.empty; Actions = Map.empty
              Findings = Map.empty; NegativeKnowledge = Map.empty; Causes = Map.empty; CorrectiveActions = Map.empty
              Criteria = Map.empty; Verifications = Map.empty; Conclusions = Map.empty; Proposals = Map.empty }
        | None, _ -> invalidOp "the first event must start the investigation"
        | Some inv, body ->
            let inv = { inv with Version = e.Sequence }
            match body with
            | InvestigationStarted _ -> inv
            | LifecycleChanged(_, target, _, _) -> { inv with Lifecycle = target }
            | ObservationRecorded(oid, t, at) -> { inv with Observations = inv.Observations |> Map.add oid { Id = oid; Original = t; ObservedAt = at; Provenance = p } }
            | HypothesisRecorded(hid, s, m) ->
                { inv with Hypotheses = inv.Hypotheses |> Map.add hid { Id = hid; Statement = s; Mechanism = m; State = Proposed; History = []; Provenance = p } }
            | HypothesisTransitioned(hid, change) ->
                { inv with Hypotheses = inv.Hypotheses |> upd hid (fun h -> { h with State = change.To; History = h.History @ [ { change with Sequence = e.Sequence } ] }) }
            | EvidenceAdmitted(eid, c, cat, src, at) ->
                { inv with Evidence = inv.Evidence |> Map.add eid { Id = eid; Content = c; Category = cat; Source = src; CapturedAt = at; Verification = Unverified; SupersededBy = None; AdmittedAt = e.Sequence; Provenance = p } }
            | EvidenceSuperseded(old, replacement, _) -> { inv with Evidence = inv.Evidence |> upd old (fun ev -> { ev with SupersededBy = Some replacement }) }
            | EvidenceVerificationAssessed(eid, s) -> { inv with Evidence = inv.Evidence |> upd eid (fun ev -> { ev with Verification = s }) }
            | EvidenceRelated(rid, eid, hid, kind, r) ->
                { inv with Relationships = inv.Relationships |> Map.add rid { Id = rid; Evidence = eid; Hypothesis = hid; Kind = kind; Rationale = r; Provenance = p } }
            | UnknownIdentified(uid, q, m) -> { inv with Unknowns = inv.Unknowns |> Map.add uid { Id = uid; Question = q; Materiality = m; State = Identified; Basis = None; Provenance = p } }
            | UnknownTransitioned(uid, s, b) -> { inv with Unknowns = inv.Unknowns |> upd uid (fun u -> { u with State = s; Basis = Some b }) }
            | AssumptionRecorded(aid, prop, m) -> { inv with Assumptions = inv.Assumptions |> Map.add aid { Id = aid; Proposition = prop; Materiality = m; State = ExplicitUnverified; Provenance = p } }
            | AssumptionTransitioned(aid, s) -> { inv with Assumptions = inv.Assumptions |> upd aid (fun a -> { a with State = s }) }
            | ContradictionRecorded(cid, s, ev, m) ->
                { inv with Contradictions = inv.Contradictions |> Map.add cid { Id = cid; Statement = s; Evidence = ev; Materiality = m; State = Detected; Basis = None; Provenance = p } }
            | ContradictionTransitioned(cid, s, b) -> { inv with Contradictions = inv.Contradictions |> upd cid (fun c -> { c with State = s; Basis = Some b }) }
            | PredictionRecorded(pid, hid, s, st, t) ->
                { inv with Predictions = inv.Predictions |> Map.add pid { Id = pid; Hypothesis = hid; Statement = s; Strength = st; Timing = t; Provenance = p } }
            | ActionRecorded(aid, k, purpose, targets) ->
                { inv with Actions = inv.Actions |> Map.add aid { Id = aid; Kind = k; Purpose = purpose; Targets = targets; State = ActionProposed; Provenance = p } }
            | ActionTransitioned(aid, s) -> { inv with Actions = inv.Actions |> upd aid (fun a -> { a with State = s }) }
            | FindingRecorded(fid, aid, r, i) -> { inv with Findings = inv.Findings |> Map.add fid { Id = fid; Action = aid; OriginalResult = r; Interpretation = i; Provenance = p } }
            | NegativeKnowledgeEstablished(nid, s, sc, b) ->
                { inv with NegativeKnowledge = inv.NegativeKnowledge |> Map.add nid { Id = nid; Statement = s; Scope = sc; Basis = b; Provenance = p } }
            | CausalAssertionRecorded(cid, o, members, m) ->
                { inv with Causes = inv.Causes |> Map.add cid { Id = cid; Outcome = o; Members = members; Mechanism = m; State = CauseProposed; Provenance = p } }
            | CausalAssertionConfirmed(cid, _) -> { inv with Causes = inv.Causes |> upd cid (fun c -> { c with State = CauseConfirmed }) }
            | CausalAssertionReopened(cid, _) -> { inv with Causes = inv.Causes |> upd cid (fun c -> { c with State = CauseReopened }) }
            | CorrectiveActionRecorded(caid, k, d, t) ->
                { inv with CorrectiveActions = inv.CorrectiveActions |> Map.add caid { Id = caid; Kind = k; Description = d; Targets = t; State = CorrectionProposed; AwaitingSince = 0L; Provenance = p } }
            | CorrectiveActionTransitioned(caid, s) ->
                { inv with CorrectiveActions = inv.CorrectiveActions |> upd caid (fun ca -> { ca with State = s; AwaitingSince = (if s = AwaitingVerificationOfEffect then e.Sequence else ca.AwaitingSince) }) }
            | CriterionRecorded(crid, caid, ex, m) -> { inv with Criteria = inv.Criteria |> Map.add crid { Id = crid; CorrectiveAction = caid; ExpectedOutcome = ex; Method = m; Provenance = p } }
            | VerificationRecorded(vid, crid, o, ev) -> { inv with Verifications = inv.Verifications |> Map.add vid { Id = vid; Criterion = crid; Outcome = o; Evidence = ev; Sequence = e.Sequence; Provenance = p } }
            | ConclusionRecorded(cid, s, st, causes, r) ->
                { inv with Conclusions = inv.Conclusions |> Map.add cid { Id = cid; Statement = s; Strength = st; Causes = causes; ResidualUncertainty = r; Sequence = e.Sequence; Provenance = p } }
            | ProposalRecorded(pid, k, t, r) ->
                { inv with Proposals = inv.Proposals |> Map.add pid { Id = pid; Kind = k; Text = t; Rationale = r; State = ProposalActive; Decision = None; Provenance = p } }
            | ProposalWasAccepted(pid, _) -> { inv with Proposals = inv.Proposals |> upd pid (fun pr -> { pr with State = ProposalAccepted }) }
            | ProposalWasRejected(pid, r) -> { inv with Proposals = inv.Proposals |> upd pid (fun pr -> { pr with State = ProposalRejected; Decision = Some r }) }

    /// Rebuild state from history (deterministic; A30).
    let replay (events: Event list) : Investigation option =
        events |> List.fold (fun s e -> Some(apply s e)) None

[<RequireQualifiedAccess>]
module Decide =
    type private R<'T> = Result<'T, DomainError list>

    let private fail code rule message : R<'T> = Error [ DomainError.create code rule message ]

    let private text field (value: string) : R<NonEmptyText> =
        NonEmptyText.create value
        |> Result.mapError (fun _ -> [ DomainError.create "VALIDATION.EMPTY" "schema" $"{field} must not be empty." ])

    let private optText (value: string option) : NonEmptyText option = value |> Option.bind NonEmptyText.tryCreate

    let private found field (map: Map<'K, 'V>) (key: 'K) : R<'V> =
        match Map.tryFind key map with
        | Some v -> Ok v
        | None -> fail "NOT_FOUND" "schema" $"{field} does not exist."

    let private absent field (map: Map<'K, 'V>) (key: 'K) : R<unit> =
        if Map.containsKey key map then fail "DUPLICATE_ID" "schema" $"{field} id already exists." else Ok()

    let private allExist field (map: Map<'K, 'V>) (keys: 'K list) : R<unit> =
        if keys |> List.forall (fun k -> Map.containsKey k map) then Ok()
        else fail "NOT_FOUND" "schema" $"A referenced {field} does not exist."

    let private structural name (target: 'S option) (from: string) : R<'S> =
        match target with
        | Some s -> Ok s
        | None -> fail "TRANSITION.ILLEGAL" "STATE-TRANSITION-TABLES" $"{name} is not legal from state {from}."

    let private both (a: R<'A>) (b: R<'B>) : R<'A * 'B> =
        match a, b with
        | Ok x, Ok y -> Ok(x, y)
        | Error e1, Error e2 -> Error(e1 @ e2)
        | Error e, _ | _, Error e -> Error e

    let private ok events : R<EventBody list> = Ok events

    let private hypothesisBody (inv: Investigation) (at: DateTimeOffset) (hid, command, reason, evidence: EvidenceId list) : R<EventBody list> =
        found "Hypothesis" inv.Hypotheses hid
        |> Result.bind (fun h ->
            let target = structural $"{command}" (Transitions.hypothesis h.State command) (string h.State)
            let why = text "Reason" reason
            let cited =
                if Transitions.requiresEvidence command && evidence.IsEmpty then
                    fail "HYPOTHESIS.EVIDENCE_REQUIRED" "R2.3" $"{command} must cite the evidence that caused it."
                else allExist "evidence" inv.Evidence evidence
            let fresh =
                match command with
                | Reopen ->
                    let falsifiedAt =
                        // The latest falsification: evidence must postdate it, not an earlier one.
                        h.History |> List.tryFindBack (fun c -> c.To = Falsified) |> Option.map (fun c -> c.Sequence) |> Option.defaultValue 0L
                    let isNew = evidence |> List.exists (fun e -> inv.Evidence |> Map.tryFind e |> Option.exists (fun ev -> ev.AdmittedAt > falsifiedAt))
                    if isNew then Ok() else fail "HYPOTHESIS.REOPEN_NEEDS_NEW_EVIDENCE" "R2.5" "Reopening needs evidence admitted after the hypothesis was falsified."
                | _ -> Ok()
            both (both target why) (both cited fresh)
            |> Result.map (fun ((target, why), _) ->
                [ HypothesisTransitioned(
                      hid,
                      { From = h.State; To = target; Command = $"{command}"; Reason = why; Evidence = evidence; At = at; Sequence = inv.Version + 1L }
                  ) ]))

    let private isDuplicateOfNegative (inv: Investigation) (statement: string) =
        let norm (s: string) = s.Trim().ToLowerInvariant()
        let s = norm statement
        (inv.Hypotheses |> Map.exists (fun _ h -> h.State = Falsified && norm h.Statement.Value = s))
        || (inv.NegativeKnowledge |> Map.exists (fun _ n -> norm n.Statement.Value = s))

    /// Commands applicable to an existing, open investigation.
    let rec private content (inv: Investigation) (env: CommandEnvelope) : R<EventBody list> =
        match env.Command with
        | RecordObservation(oid, original, observedAt) ->
            both (absent "Observation" inv.Observations oid) (text "Observation" original)
            |> Result.map (fun (_, t) -> [ ObservationRecorded(oid, t, observedAt) ])
        | RecordHypothesis(hid, statement, mechanism) ->
            both (absent "Hypothesis" inv.Hypotheses hid) (text "Hypothesis statement" statement)
            |> Result.map (fun (_, t) -> [ HypothesisRecorded(hid, t, optText mechanism) ])
        | TransitionHypothesis(hid, command, reason, evidence) -> hypothesisBody inv env.RequestedAt (hid, command, reason, evidence)
        | AdmitEvidence(eid, input, category, source, capturedAt) ->
            let contentR =
                match input with
                | TextInput t -> text "Evidence text" t |> Result.map TextEvidence
                | UrlInput u ->
                    let scheme = if u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) then Uri.IsWellFormedUriString(u, UriKind.Absolute) else false
                    match scheme with
                    | true -> Ok(ArtifactEvidence u)
                    | false -> fail "EVIDENCE.URL" "R3.5" "An artifact reference must be an absolute http(s) URL."
            both (absent "Evidence" inv.Evidence eid) (both contentR (text "Evidence source" source))
            |> Result.map (fun (_, (c, s)) -> [ EvidenceAdmitted(eid, c, category, s, capturedAt) ])
        | SupersedeEvidence(old, replacement, reason) ->
            both (found "Evidence" inv.Evidence old) (both (found "Replacement evidence" inv.Evidence replacement) (text "Reason" reason))
            |> Result.bind (fun (o, (_, why)) ->
                if o.SupersededBy.IsSome then fail "EVIDENCE.ALREADY_SUPERSEDED" "STATE-TRANSITION-TABLES §4" "Evidence is already superseded."
                elif old = replacement then fail "EVIDENCE.SELF" "schema" "Evidence cannot supersede itself."
                else ok [ EvidenceSuperseded(old, replacement, why) ])
        | VerifyEvidence(eid, status) ->
            found "Evidence" inv.Evidence eid |> Result.map (fun _ -> [ EvidenceVerificationAssessed(eid, status) ])
        | RelateEvidence(rid, eid, hid, kind, rationale) ->
            both (absent "Relationship" inv.Relationships rid) (both (found "Evidence" inv.Evidence eid) (found "Hypothesis" inv.Hypotheses hid))
            |> Result.map (fun _ -> [ EvidenceRelated(rid, eid, hid, kind, optText rationale) ])
        | IdentifyUnknown(uid, question, materiality) ->
            both (absent "Unknown" inv.Unknowns uid) (text "Question" question)
            |> Result.map (fun (_, q) -> [ UnknownIdentified(uid, q, materiality) ])
        | TransitionUnknown(uid, command, basis) ->
            found "Unknown" inv.Unknowns uid
            |> Result.bind (fun u -> both (structural $"{command}" (Transitions.unknown u.State command) (string u.State)) (text "Basis" basis))
            |> Result.map (fun (s, b) -> [ UnknownTransitioned(uid, s, b) ])
        | RecordAssumption(aid, proposition, materiality) ->
            both (absent "Assumption" inv.Assumptions aid) (text "Assumption" proposition)
            |> Result.map (fun (_, p) -> [ AssumptionRecorded(aid, p, materiality) ])
        | TransitionAssumption(aid, command) ->
            found "Assumption" inv.Assumptions aid
            |> Result.bind (fun a -> structural $"{command}" (Transitions.assumption a.State command) (string a.State))
            |> Result.map (fun s -> [ AssumptionTransitioned(aid, s) ])
        | RecordContradiction(cid, statement, evidence, materiality) ->
            both (absent "Contradiction" inv.Contradictions cid) (both (text "Contradiction" statement) (allExist "evidence" inv.Evidence evidence))
            |> Result.map (fun (_, (s, _)) -> [ ContradictionRecorded(cid, s, evidence, materiality) ])
        | TransitionContradiction(cid, command, basis) ->
            found "Contradiction" inv.Contradictions cid
            |> Result.bind (fun c -> both (structural $"{command}" (Transitions.contradiction c.State command) (string c.State)) (text "Resolution basis" basis))
            |> Result.map (fun (s, b) -> [ ContradictionTransitioned(cid, s, b) ])
        | RecordPrediction(pid, hid, statement, strength, timing) ->
            both (absent "Prediction" inv.Predictions pid) (both (found "Hypothesis" inv.Hypotheses hid) (text "Prediction" statement))
            |> Result.map (fun (_, (_, s)) ->
                // A11: a prediction made after a relevant finding exists is retrospective, whatever was requested.
                let findingExists =
                    inv.Findings |> Map.exists (fun _ f -> inv.Actions |> Map.tryFind f.Action |> Option.exists (fun a -> a.Targets.IsEmpty || a.Targets |> List.contains hid))
                let actual = if findingExists then Retrospective else timing
                [ PredictionRecorded(pid, hid, s, strength, actual) ])
        | RecordAction(aid, kind, purpose, targets) ->
            both (absent "Action" inv.Actions aid) (both (text "Purpose" purpose) (allExist "hypothesis" inv.Hypotheses targets))
            |> Result.map (fun (_, (p, _)) -> [ ActionRecorded(aid, kind, p, targets) ])
        | TransitionAction(aid, command) ->
            found "Action" inv.Actions aid
            |> Result.bind (fun a -> structural $"{command}" (Transitions.action a.State command) (string a.State))
            |> Result.map (fun s -> [ ActionTransitioned(aid, s) ])
        | RecordFinding(fid, aid, result, interpretation) ->
            both (absent "Finding" inv.Findings fid) (both (found "Action" inv.Actions aid) (text "Result" result))
            |> Result.bind (fun (_, (a, r)) ->
                match a.State with
                | ActionInProgress ->
                    ok [ FindingRecorded(fid, aid, r, optText interpretation); ActionTransitioned(aid, ActionCompleted) ]
                | ActionApproved ->
                    // STATE-TRANSITION-TABLES §5: Approved -> InProgress -> Completed, explicitly.
                    ok [ ActionTransitioned(aid, ActionInProgress); FindingRecorded(fid, aid, r, optText interpretation); ActionTransitioned(aid, ActionCompleted) ]
                | ActionCompleted -> ok [ FindingRecorded(fid, aid, r, optText interpretation) ]
                | s -> fail "ACTION.NOT_EXECUTABLE" "STATE-TRANSITION-TABLES §5" $"A finding cannot be recorded for an action that is {s}.")
        | EstablishNegativeKnowledge(nid, statement, scope, basis) ->
            both (absent "Negative knowledge" inv.NegativeKnowledge nid) (both (text "Statement" statement) (both (text "Scope" scope) (allExist "evidence" inv.Evidence basis)))
            |> Result.bind (fun (_, (s, (sc, _))) ->
                if basis.IsEmpty then fail "NEGATIVE.BASIS_REQUIRED" "R5.3" "Negative knowledge must cite the evidence that established it."
                else ok [ NegativeKnowledgeEstablished(nid, s, sc, basis) ])
        | RecordCausalAssertion(cid, outcome, members, mechanism) ->
            both (absent "Causal assertion" inv.Causes cid) (both (text "Outcome" outcome) (allExist "hypothesis" inv.Hypotheses (members |> List.map (fun m -> m.Hypothesis))))
            |> Result.bind (fun (_, (o, _)) ->
                if members.IsEmpty then fail "CAUSE.NO_MEMBERS" "R6.3" "A causal assertion needs at least one member."
                else ok [ CausalAssertionRecorded(cid, o, members, optText mechanism) ])
        | ConfirmCause cid ->
            match Policy.causeBlockers inv cid with
            | [] -> ok [ CausalAssertionConfirmed(cid, Policy.id) ]
            | blockers -> Error blockers
        | ReopenCause(cid, reason) ->
            both (found "Causal assertion" inv.Causes cid) (text "Reason" reason)
            |> Result.bind (fun (c, r) ->
                if c.State = CauseConfirmed then ok [ CausalAssertionReopened(cid, r) ]
                else fail "TRANSITION.ILLEGAL" "STATE-TRANSITION-TABLES §10" $"Only a confirmed cause can be reopened (it is {c.State}).")
        | RecordCorrectiveAction(caid, kind, description, targets) ->
            both (absent "Corrective action" inv.CorrectiveActions caid) (both (text "Description" description) (allExist "cause" inv.Causes targets))
            |> Result.map (fun (_, (d, _)) -> [ CorrectiveActionRecorded(caid, kind, d, targets) ])
        | TransitionCorrection(caid, command) ->
            found "Corrective action" inv.CorrectiveActions caid
            |> Result.bind (fun ca ->
                structural $"{command}" (Transitions.correction ca.State command) (string ca.State)
                |> Result.bind (fun target ->
                    let hasCriteria = inv.Criteria |> Map.exists (fun _ c -> c.CorrectiveAction = caid)
                    if target = AwaitingVerificationOfEffect && not hasCriteria then
                        fail "CORRECTION.NO_CRITERIA" "R7.3" "Define explicit success criteria before verifying."
                    else ok [ CorrectiveActionTransitioned(caid, target) ]))
        | RecordCriterion(crid, caid, expected, method) ->
            both (absent "Criterion" inv.Criteria crid) (both (found "Corrective action" inv.CorrectiveActions caid) (text "Expected outcome" expected))
            |> Result.map (fun (_, (_, e)) -> [ CriterionRecorded(crid, caid, e, optText method) ])
        | RecordVerification(vid, crid, outcome, evidence) ->
            both (absent "Verification" inv.Verifications vid) (both (found "Criterion" inv.Criteria crid) (allExist "evidence" inv.Evidence evidence))
            |> Result.bind (fun (_, (crit, _)) ->
                let ca = inv.CorrectiveActions[crit.CorrectiveAction]
                if ca.State <> AwaitingVerificationOfEffect then
                    fail "VERIFICATION.NOT_AWAITING" "R7.4" $"The corrective action is {ca.State}; mark it implemented and start verification first (implementation is not verification)."
                elif outcome = Satisfied && evidence.IsEmpty then
                    fail "VERIFICATION.EVIDENCE_REQUIRED" "R7.4" "A satisfied verification must cite evidence."
                elif outcome = Satisfied && not (evidence |> List.forall (Policy.freshEvidence inv ca.AwaitingSince)) then
                    fail "VERIFICATION.EVIDENCE_PREDATES_ROUND" "R7.4" "Verification evidence must be gathered after verification started and still be current (not superseded)."
                else
                    let recorded = VerificationRecorded(vid, crid, outcome, evidence)
                    let after = Evolve.applyBody inv (inv.Version + 1L) env recorded
                    let follow =
                        match outcome with
                        | Failed ->
                            [ CorrectiveActionTransitioned(ca.Id, VerificationFailed) ]
                            @ (if inv.Lifecycle = AwaitingVerification then [ LifecycleChanged(AwaitingVerification, Active, "VerificationFailed", Some "A verification failed; work reopens (A21).") ] else [])
                        | Satisfied when Policy.allCriteriaSatisfied after ca.Id -> [ CorrectiveActionTransitioned(ca.Id, VerifiedEffective) ]
                        | _ -> []
                    ok (recorded :: follow))
        | RecordConclusion(cid, statement, strength, causes, residual) ->
            both (absent "Conclusion" inv.Conclusions cid) (both (text "Conclusion" statement) (allExist "cause" inv.Causes causes))
            |> Result.bind (fun (_, (s, _)) ->
                let confirmed = causes |> List.exists (fun c -> inv.Causes[c].State = CauseConfirmed)
                match strength with
                | ConfirmedCause when not confirmed ->
                    fail "CONCLUSION.UNSUPPORTED" "R6.1" "A conclusion cannot claim a confirmed cause before the cause is confirmed."
                | _ when residual |> Option.forall String.IsNullOrWhiteSpace && (not (Policy.unresolvedMaterialUnknowns inv).IsEmpty || inv.Unknowns |> Map.exists (fun _ u -> u.State = AcceptedResidual)) ->
                    fail "CONCLUSION.RESIDUAL_REQUIRED" "R6.4" "State the residual uncertainty: unknowns remain open or accepted as residual."
                | _ -> ok [ ConclusionRecorded(cid, s, strength, causes, optText residual) ])
        | RecordProposal(pid, kind, proposalText, rationale) ->
            both (absent "Proposal" inv.Proposals pid) (text "Proposal" proposalText)
            |> Result.bind (fun (_, t) ->
                match env.Actor.Kind with
                | Agent _ when kind = ProposedHypothesis && isDuplicateOfNegative inv proposalText ->
                    fail "PROPOSAL.REPEATS_NEGATIVE_KNOWLEDGE" "R5.4" "This explanation was already falsified or eliminated; cite new evidence and reopen instead."
                | _ -> ok [ ProposalRecorded(pid, kind, t, optText rationale) ])
        | AcceptProposal(pid, targetId) ->
            found "Proposal" inv.Proposals pid
            |> Result.bind (fun p ->
                if p.State <> ProposalActive then fail "PROPOSAL.NOT_ACTIVE" "STATE-TRANSITION-TABLES §12" $"The proposal is {p.State}."
                else
                    match env.Actor.Kind with
                    | Agent _ -> fail "PROPOSAL.AGENT_CANNOT_ACCEPT" "D-004" "Only a human can accept an AI proposal."
                    | _ ->
                        let target =
                            match p.Kind with
                            | ProposedHypothesis -> RecordHypothesis(Id.create targetId, p.Text.Value, None)
                            | ProposedUnknown -> IdentifyUnknown(Id.create targetId, p.Text.Value, Material)
                            | ProposedAction -> RecordAction(Id.create targetId, Test, p.Text.Value, [])
                        // A blocked target command does not produce ProposalAccepted.
                        content inv { env with Command = target }
                        |> Result.map (fun events -> events @ [ ProposalWasAccepted(pid, targetId) ]))
        | RejectProposal(pid, reason) ->
            both (found "Proposal" inv.Proposals pid) (text "Reason" reason)
            |> Result.bind (fun (p, r) ->
                if p.State <> ProposalActive then fail "PROPOSAL.NOT_ACTIVE" "STATE-TRANSITION-TABLES §12" $"The proposal is {p.State}."
                else ok [ ProposalWasRejected(pid, r) ])
        | StartInvestigation _ -> fail "INVESTIGATION.EXISTS" "schema" "The investigation already exists."
        | ActivateInvestigation | MarkAwaitingEvidence _ | MarkAwaitingVerification | ResolveInvestigation | CloseInvestigation | ReopenInvestigation _ ->
            fail "INTERNAL" "decide" "lifecycle commands are handled separately"

    let private lifecycleCommand (inv: Investigation) (env: CommandEnvelope) : R<EventBody list> =
        let name =
            match env.Command with
            | ActivateInvestigation -> "ActivateInvestigation"
            | MarkAwaitingEvidence _ -> "MarkAwaitingEvidence"
            | MarkAwaitingVerification -> "MarkAwaitingVerification"
            | ResolveInvestigation -> "ResolveInvestigation"
            | CloseInvestigation -> "CloseInvestigation"
            | ReopenInvestigation _ -> "ReopenInvestigation"
            | _ -> "?"
        structural name (Transitions.lifecycle inv.Lifecycle env.Command) (string inv.Lifecycle)
        |> Result.bind (fun target ->
            match env.Command with
            | MarkAwaitingEvidence reason ->
                text "Reason" reason |> Result.map (fun r -> [ LifecycleChanged(inv.Lifecycle, target, name, Some r.Value) ])
            | ReopenInvestigation basis ->
                text "Reopening basis" basis |> Result.map (fun b -> [ LifecycleChanged(inv.Lifecycle, target, name, Some b.Value) ])
            | MarkAwaitingVerification ->
                let work = inv.CorrectiveActions |> Map.exists (fun _ ca -> ca.State = Implemented || ca.State = AwaitingVerificationOfEffect)
                if work then ok [ LifecycleChanged(inv.Lifecycle, target, name, None) ]
                else fail "LIFECYCLE.NO_VERIFICATION_WORK" "STATE-TRANSITION-TABLES §1" "No implemented corrective action awaits verification."
            | ResolveInvestigation ->
                match Policy.resolutionBlockers inv with
                | [] -> ok [ LifecycleChanged(inv.Lifecycle, target, name, None) ]
                | blockers -> Error blockers
            | _ -> ok [ LifecycleChanged(inv.Lifecycle, target, name, None) ])

    /// The single decision path (STATE-TRANSITION-TABLES §16 order: schema,
    /// version, invariants, structure, policy). Pure.
    let decide (state: Investigation option) (env: CommandEnvelope) : R<EventBody list> =
        match env.Actor.Kind, env.Command with
        // D-004 / R15.2: an AI agent proposes; it never writes authoritative state directly.
        | Agent _, cmd when (match cmd with RecordProposal _ -> false | _ -> true) ->
            fail "AGENT.PROPOSAL_ONLY" "D-004" "An AI agent may only record proposals; a human must accept them."
        | _ ->
        match state, env.Command with
        | None, StartInvestigation(iid, title, oid, original) ->
            if iid <> env.Investigation then fail "ENVELOPE.MISMATCH" "schema" "Investigation id does not match the envelope."
            else
                let titleText = if String.IsNullOrWhiteSpace title then original else title
                both (text "Title" titleText) (text "Initial observation" original)
                |> Result.map (fun (t, o) -> [ InvestigationStarted(t, oid, o) ])
        | None, _ -> fail "INVESTIGATION.NOT_FOUND" "schema" "Start the investigation first."
        | Some inv, _ when env.Investigation <> inv.Id ->
            fail "ENVELOPE.MISMATCH" "schema" "The command names a different investigation than the one loaded."
        | Some inv, _ ->
            match env.ExpectedVersion with
            | Some v when v <> inv.Version ->
                fail "VERSION.STALE" "STATE-TRANSITION-TABLES §16.3" $"The investigation changed (expected version {v}, current {inv.Version}); reload and retry."
            | _ ->
                match env.Command with
                | ActivateInvestigation | MarkAwaitingEvidence _ | MarkAwaitingVerification | ResolveInvestigation | CloseInvestigation | ReopenInvestigation _ ->
                    lifecycleCommand inv env
                | _ when not (Transitions.isOpen inv.Lifecycle) ->
                    fail "INVESTIGATION.NOT_OPEN" "D-005" $"The investigation is {inv.Lifecycle}; reopen it to change its content."
                | _ -> content inv env

    /// Evaluate a command without emitting anything, for UI capability display (single decision path).
    let probe (inv: Investigation) (actor: Actor) (at: DateTimeOffset) (command: Command) : Result<unit, DomainError list> =
        decide (Some inv) { CommandId = Guid.Empty; Investigation = inv.Id; ExpectedVersion = None; Actor = actor; RequestedAt = at; Command = command }
        |> Result.map ignore

    /// decide + stamp: semantic events with sequence numbers.
    let execute (state: Investigation option) (env: CommandEnvelope) : Result<Event list, DomainError list> =
        let version = state |> Option.map (fun i -> i.Version) |> Option.defaultValue 0L
        decide state env
        |> Result.map (List.mapi (fun i body ->
            { Sequence = version + int64 i + 1L
              Investigation = env.Investigation
              CommandId = env.CommandId
              Actor = env.Actor
              At = env.RequestedAt
              Body = body }))

/// Admissibility of a loaded history (local or remote), by REPLAY through the single
/// decision path (RC3 review C-R3-1; DEC-04). Events are grouped by CommandId; each group
/// is mapped back to the command it records, re-decided against the prior state with the
/// recorded actor and time, and must reproduce exactly the recorded events. Every rule the
/// decision path enforces (open-only content D-005, id uniqueness, agent authority D-004,
/// cause/resolution gates I-19/I-20, verification freshness R7.4, prediction timing A11,
/// repetition refusal R5.4) therefore also binds stored and remote histories.
[<RequireQualifiedAccess>]
module History =
    let private v (t: NonEmptyText) = t.Value
    let private ov (t: NonEmptyText option) = t |> Option.map v

    let private content c =
        match c with
        | TextEvidence t -> TextInput t.Value
        | ArtifactEvidence u -> UrlInput u

    /// The command a group of events records, or None when no single command could emit it.
    let commandOf (investigation: InvestigationId) (bodies: EventBody list) : Command option =
        let has f = bodies |> List.tryPick f
        let finding = has (function FindingRecorded(f, a, r, i) -> Some(RecordFinding(f, a, v r, ov i)) | _ -> None)
        let verification = has (function VerificationRecorded(vid, c, o, ev) -> Some(RecordVerification(vid, c, o, ev)) | _ -> None)
        let accepted = has (function ProposalWasAccepted(p, t) -> Some(AcceptProposal(p, t)) | _ -> None)
        match finding, verification, accepted, bodies with
        | Some c, _, _, _ | _, Some c, _, _ | _, _, Some c, _ -> Some c
        | _, _, _, [] -> None
        | _, _, _, first :: _ ->
            match first with
            | InvestigationStarted(t, oid, o) -> Some(StartInvestigation(investigation, v t, oid, v o))
            | LifecycleChanged(_, _, command, reason) ->
                match command, reason with
                | "ActivateInvestigation", _ -> Some ActivateInvestigation
                | "MarkAwaitingEvidence", Some r -> Some(MarkAwaitingEvidence r)
                | "MarkAwaitingVerification", _ -> Some MarkAwaitingVerification
                | "ResolveInvestigation", _ -> Some ResolveInvestigation
                | "CloseInvestigation", _ -> Some CloseInvestigation
                | "ReopenInvestigation", Some r -> Some(ReopenInvestigation r)
                | _ -> None
            | ObservationRecorded(o, t, at) -> Some(RecordObservation(o, v t, at))
            | HypothesisRecorded(h, s, m) -> Some(RecordHypothesis(h, v s, ov m))
            | HypothesisTransitioned(h, change) ->
                [ MarkPlausible; MarkSupported; MarkWeakened; Falsify; Supersede; Reopen ]
                |> List.tryFind (fun c -> string c = change.Command)
                |> Option.map (fun c -> TransitionHypothesis(h, c, v change.Reason, change.Evidence))
            | EvidenceAdmitted(e, c, cat, src, at) -> Some(AdmitEvidence(e, content c, cat, v src, at))
            | EvidenceSuperseded(o, r, reason) -> Some(SupersedeEvidence(o, r, v reason))
            | EvidenceVerificationAssessed(e, st) -> Some(VerifyEvidence(e, st))
            | EvidenceRelated(r, e, h, k, why) -> Some(RelateEvidence(r, e, h, k, ov why))
            | UnknownIdentified(u, q, m) -> Some(IdentifyUnknown(u, v q, m))
            | UnknownTransitioned(u, st, basis) ->
                match st with
                | Investigating -> Some StartInvestigatingUnknown
                | UnknownResolved -> Some ResolveUnknown
                | AcceptedResidual -> Some AcceptUnknownAsResidual
                | _ -> None
                |> Option.map (fun c -> TransitionUnknown(u, c, v basis))
            | AssumptionRecorded(a, p, m) -> Some(RecordAssumption(a, v p, m))
            | AssumptionTransitioned(a, st) ->
                match st with
                | ProvisionallyAccepted -> Some ProvisionallyAcceptAssumption
                | AssumptionVerified -> Some VerifyAssumption
                | Rejected -> Some RejectAssumption
                | _ -> None
                |> Option.map (fun c -> TransitionAssumption(a, c))
            | ContradictionRecorded(c, s, ev, m) -> Some(RecordContradiction(c, v s, ev, m))
            | ContradictionTransitioned(c, st, basis) ->
                match st with
                | Triaged -> Some TriageContradiction
                | ContradictionResolved -> Some ResolveContradiction
                | ContradictionAcceptedResidual -> Some AcceptContradictionAsResidual
                | _ -> None
                |> Option.map (fun cmd -> TransitionContradiction(c, cmd, v basis))
            | PredictionRecorded(p, h, s, st, timing) -> Some(RecordPrediction(p, h, v s, st, timing))
            | ActionRecorded(a, k, p, t) -> Some(RecordAction(a, k, v p, t))
            | ActionTransitioned(a, st) ->
                match st with
                | ActionApproved -> Some ApproveAction
                | ActionRejected -> Some RejectAction
                | ActionInProgress -> Some StartAction
                | ActionCancelled -> Some CancelAction
                | _ -> None
                |> Option.map (fun c -> TransitionAction(a, c))
            | NegativeKnowledgeEstablished(n, s, sc, b) -> Some(EstablishNegativeKnowledge(n, v s, v sc, b))
            | CausalAssertionRecorded(c, o, m, mech) -> Some(RecordCausalAssertion(c, v o, m, ov mech))
            | CausalAssertionConfirmed(c, _) -> Some(ConfirmCause c)
            | CausalAssertionReopened(c, r) -> Some(ReopenCause(c, v r))
            | CorrectiveActionRecorded(c, k, d, t) -> Some(RecordCorrectiveAction(c, k, v d, t))
            | CorrectiveActionTransitioned(c, st) ->
                match st with
                | CorrectionApproved -> Some ApproveCorrection
                | CorrectionInProgress -> Some StartCorrection
                | Implemented -> Some MarkImplemented
                | AwaitingVerificationOfEffect -> Some StartVerificationOfEffect
                | ReworkRequired -> Some RequireRework
                | CorrectionCancelled -> Some CancelCorrection
                | _ -> None
                |> Option.map (fun cmd -> TransitionCorrection(c, cmd))
            | CriterionRecorded(c, ca, e, m) -> Some(RecordCriterion(c, ca, v e, ov m))
            | ConclusionRecorded(c, s, st, causes, r) -> Some(RecordConclusion(c, v s, st, causes, ov r))
            | ProposalRecorded(p, k, t, r) -> Some(RecordProposal(p, k, v t, ov r))
            | ProposalWasRejected(p, r) -> Some(RejectProposal(p, v r))
            | FindingRecorded _ | VerificationRecorded _ | ProposalWasAccepted _ -> None

    /// Consecutive events of one command.
    let private groups (events: Event list) : Event list list =
        events
        |> List.fold (fun acc e ->
            match acc with
            | (head :: _ as g) :: rest when head.CommandId = e.CommandId -> (e :: g) :: rest
            | _ -> [ e ] :: acc) []
        |> List.map List.rev
        |> List.rev

    let private codeFor (bodies: EventBody list) =
        match bodies with
        | CausalAssertionConfirmed _ :: _ -> "HISTORY.UNGATED_CONFIRMATION"
        | LifecycleChanged(_, Resolved, _, _) :: _ -> "HISTORY.UNGATED_RESOLUTION"
        | _ -> "HISTORY.INADMISSIBLE_EVENT"

    /// Every violation in the history; empty means it is exactly what the decision path emits.
    let validate (events: Event list) : DomainError list =
        let step (state: Investigation option, problems: DomainError list) (group: Event list) =
            let first = List.head group
            let expected = (state |> Option.map (fun i -> i.Version) |> Option.defaultValue 0L) + 1L
            let sequence =
                group |> List.mapi (fun i e -> e, expected + int64 i)
                |> List.filter (fun (e, want) -> e.Sequence <> want)
                |> List.map (fun (e, want) -> DomainError.create "HISTORY.SEQUENCE" "D-005" $"Event {e.Sequence} found where {want} was expected.")
            let bodies = group |> List.map (fun e -> e.Body)
            let consistent = group |> List.forall (fun e -> e.Actor = first.Actor && e.At = first.At && e.Investigation = first.Investigation)
            let envelope command =
                { CommandId = first.CommandId; Investigation = first.Investigation; ExpectedVersion = None
                  Actor = first.Actor; RequestedAt = first.At; Command = command }
            let verdict =
                match state, commandOf first.Investigation bodies with
                | None, _ when (match bodies with InvestigationStarted _ :: _ -> false | _ -> true) ->
                    [ DomainError.create "HISTORY.NO_START" "D-005" "The history does not begin with InvestigationStarted." ]
                | _, None -> [ DomainError.create "HISTORY.INADMISSIBLE_EVENT" "DEC-04" $"Events from {first.Sequence} are not what any single command emits." ]
                | _, Some command when not consistent ->
                    [ DomainError.create "HISTORY.INADMISSIBLE_EVENT" "DEC-04" $"Events of command {first.CommandId} disagree on actor, time or investigation ({command})." ]
                | _, Some command ->
                    match Decide.decide state (envelope command) with
                    | Error refusals ->
                        refusals |> List.map (fun r -> { r with Code = codeFor bodies; Message = $"Event {first.Sequence} would be refused now ({r.Code}): {r.Message}" })
                    | Ok decided when decided <> bodies ->
                        [ DomainError.create "HISTORY.INADMISSIBLE_EVENT" "DEC-04" $"Events from {first.Sequence} differ from what the command emits." ]
                    | Ok _ -> []
            let next = group |> List.fold (fun s e -> match s, e.Body with | None, InvestigationStarted _ | Some _, _ -> Some(Evolve.apply s e) | None, _ -> None) state
            next, problems @ sequence @ verdict
        events |> groups |> List.fold step (None, []) |> snd

