namespace Causa.Engine

open System
open System.Globalization
open Limen.Contract.Core
open IndyInitRootCauseInvestigator.Engine
open Causa.Contract

/// The projection: the complete, flat view the page binds to (Limen ViewState). Every key is present in every
/// projection. All investigative facts are verbatim domain values from one aggregate version (UI-013); legality and
/// "next action" come from Capabilities (the domain). Nothing here invents confidence, severity or owners.
/// The key catalogue is documented in docs/architecture/VIEW-CONTRACT.md.
[<RequireQualifiedAccess>]
module View =
    let private t (s: string) = ViewValue.Text s
    let private b (v: bool) = ViewValue.Flag v
    let private n (v: int64) = ViewValue.Number(float v)
    let private items (xs: (string * ViewPrimitive) list list) = ViewValue.Items(xs |> List.map Map.ofList)
    let private pt (s: string) = ViewPrimitive.Text s
    let private pb (v: bool) = ViewPrimitive.Flag v
    let private pn (v: int64) = ViewPrimitive.Number(float v)

    let private time (d: DateTimeOffset) = d.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
    let private optTime (d: DateTimeOffset option) = d |> Option.map time |> Option.defaultValue "Not recorded"
    let private short (sha: string) = if sha.Length >= 7 then sha.Substring(0, 7) else sha

    let toneKey =
        function
        | Neutral -> "neutral"
        | Positive -> "positive"
        | Attention -> "attention"
        | Critical -> "critical"

    /// Semantic tone for lifecycle (paired with a text label everywhere; never color alone).
    let private lifecycleTone =
        function
        | Draft | Active -> Positive
        | AwaitingEvidence | AwaitingVerification -> Attention
        | Resolved | Closed | Archived -> Neutral

    let private hypothesisTone =
        function
        | Supported -> Positive
        | Proposed | Plausible -> Neutral
        | Weakened -> Attention
        | Falsified | Superseded -> Neutral

    let private isOpenUnknown (u: Unknown) = match u.State with Identified | Investigating -> true | _ -> false
    let private isOpenContradiction (c: Contradiction) =
        match c.State with Detected | Triaged | ContradictionInvestigating -> true | _ -> false

    let private href (app: App) (route: Route) = Route.format route

    // ------------------------------------------------------------------------------------------------------------
    // Connection and sync
    // ------------------------------------------------------------------------------------------------------------

    let private connection (app: App) =
        let state, label, detail, tone =
            match app.Connection with
            | Disconnected -> "disconnected", "Not connected", "No data repository is connected. Nothing is loaded.", Neutral
            | Connecting(target, _, stage, _, _) ->
                let step =
                    match stage with
                    | CheckingRepository -> "Checking access to the repository"
                    | ReadingBranch -> "Reading the branch"
                    | ReadingCommit -> "Reading the branch head"
                    | ListingTree -> "Listing investigations"
                "connecting", "Connecting…", step + " (" + Target.identity target + ").", Attention
            | Connected l ->
                "connected", (if l.Access.CanPush && Target.writable l.Target then "Connected · read & write" else "Connected · read-only"),
                Target.identity l.Target + " at " + short l.Head.Commit, Positive
            | Refused(target, refusal) ->
                let kind, text =
                    match refusal with
                    | AccessDenied s -> "unauthorized", s
                    | NotFoundRefusal s -> "unauthorized", s
                    | OfflineRefusal s -> "offline", s
                    | RateLimited s -> "unauthorized", s
                    | Incompatible s -> "incompatible", s
                    | ProviderError s -> "failed", s
                kind, (match kind with "offline" -> "Offline" | "incompatible" -> "Incompatible data" | "failed" -> "Connection failed" | _ -> "Access denied"),
                text + " (" + Target.identity target + ")", Critical
        let f k = app.ConnectForm |> Map.tryFind k |> Option.defaultValue ""
        [ "connState", t state
          "connLabel", t label
          "connDetail", t detail
          "connTone", t (toneKey tone)
          "isDisconnected", b (match app.Connection with Disconnected | Refused _ -> true | _ -> false)
          "isConnecting", b (match app.Connection with Connecting _ -> true | _ -> false)
          "isConnected", b (match app.Connection with Connected _ -> true | _ -> false)
          "isRefused", b (match app.Connection with Refused _ -> true | _ -> false)
          "formOwner", t (f "owner")
          "formRepo", t (f "repo")
          "formBranch", t (f "branch")
          "formRoot", t (f "root") ]

    let private syncView (app: App) =
        let state, label, detail, tone, busy =
            match App.live app with
            | None -> "none", "No repository", "", Neutral, false
            | Some l ->
                match l.Sync with
                | Idle when l.Refreshing -> "loading", "Reloading", "Reading the latest version from GitHub.", Attention, true
                | Idle ->
                    match l.LastVerified with
                    | Some(commit, at) -> "synced", "Synchronized", "Last verified write " + short commit + " at " + time at + ".", Positive, false
                    | None -> "clean", "Up to date", "Showing the verified state of " + short l.Head.Commit + ". No local changes.", Positive, false
                | InFlight(w, stage) ->
                    let step =
                        match stage with
                        | CreatingBlobs | CreatingTree | CreatingCommit -> "Preparing one atomic commit"
                        | UpdatingRef -> "Updating the branch (fast-forward only)"
                        | ReloadingRef | ReloadingCommit | ReloadingTree -> "The branch moved; checking whether this investigation changed"
                        | Reconciling -> "Confirming whether GitHub received the update"
                        | VerifyingRef | VerifyingCommit | VerifyingTree | VerifyingBlobs -> "Verifying by reading the commit back"
                    "syncing", "Saving to GitHub…", w.Description + ": " + step + ". Shown locally; not yet confirmed.", Attention, true
                | Held(w, hold) ->
                    match hold with
                    | NotSent reason -> "queued", "Not saved (offline)", w.Description + " is held in this tab only. " + reason, Attention, false
                    | Conflict detail -> "conflict", "Conflict", detail + " Your change is preserved here.", Critical, false
                    | WriteDenied detail -> "failed", "Write refused", detail + " Your change is preserved here.", Critical, false
                    | WriteFailed detail -> "failed", "Save failed", detail + " Your change is preserved here.", Critical, false
                    | Unknown detail -> "unknown", "Outcome unknown", detail + " Confirm before retrying.", Critical, false
        let held hold = match App.live app with Some { Sync = Held(_, h) } -> hold h | _ -> false
        [ "syncState", t state
          "syncLabel", t label
          "syncDetail", t detail
          "syncTone", t (toneKey tone)
          "syncBusy", b busy
          "hasHeld", b (held (fun _ -> true))
          "canRetry", b (held (function NotSent _ | WriteFailed _ | WriteDenied _ -> true | _ -> false))
          "canReapply", b (held (function Conflict _ -> true | _ -> false))
          "canReconcile", b (held (function Unknown _ -> true | _ -> false))
          "canDiscard", b (held (fun _ -> true))
          "canRefresh", b (match App.live app with Some l -> (match l.Sync with InFlight _ -> false | _ -> not l.Refreshing) | None -> false)
          "refreshDisabled", b (match App.live app with Some l -> (match l.Sync with InFlight _ -> true | _ -> l.Refreshing) | None -> true) ]

    // ------------------------------------------------------------------------------------------------------------
    // Dashboard
    // ------------------------------------------------------------------------------------------------------------

    let private dashboard (app: App) =
        let rows =
            match App.live app with
            | None -> []
            | Some l ->
                l.Catalog
                |> List.map (fun e ->
                    let common status text = [ "id", pt e.Id; "status", pt status; "statusText", pt text; "href", pt (href app (InvestigationRoute(e.Id, Overview, None))) ]
                    match e.Status with
                    | Valid _ ->
                        let loaded, local = Session.displayed l e.Id |> Option.defaultWith (fun () -> failwith "valid entry")
                        let inv = loaded.State
                        let updated = loaded.Events |> List.last |> fun ev -> ev.At
                        let openContradictions = inv.Contradictions |> Map.filter (fun _ c -> isOpenContradiction c) |> Map.count
                        let openUnknowns = inv.Unknowns |> Map.filter (fun _ u -> isOpenUnknown u) |> Map.count
                        common "valid" (if local then "Local change not yet confirmed" else "Verified")
                        @ [ "title", pt inv.Title.Value
                            "lifecycle", pt (Labels.lifecycle inv.Lifecycle)
                            "lifecycleTone", pt (toneKey (lifecycleTone inv.Lifecycle))
                            "version", pn inv.Version
                            "updated", pt (time updated)
                            "updatedSort", pt (updated.UtcDateTime.ToString("o"))
                            "hypotheses", pn (int64 inv.Hypotheses.Count)
                            "evidence", pn (int64 inv.Evidence.Count)
                            "contradictions", pn (int64 openContradictions)
                            "unknowns", pn (int64 openUnknowns)
                            "hasContradictions", pb (openContradictions > 0)
                            "hasUnknowns", pb (openUnknowns > 0)
                            "nextAction", pt (Capabilities.nextLifecycle app.Context.Now inv)
                            "isValid", pb true; "isInvalid", pb false; "isLoading", pb false; "errorCode", pt ""; "errorText", pt "" ]
                    | Fetching ->
                        common "loading" "Loading and validating…"
                        @ [ "title", pt ("Investigation " + e.Id.Substring(0, 8)); "lifecycle", pt "—"; "lifecycleTone", pt "neutral"; "version", pn 0L; "updated", pt "—"
                            "updatedSort", pt ""; "hypotheses", pn 0L; "evidence", pn 0L; "contradictions", pn 0L; "unknowns", pn 0L; "hasContradictions", pb false
                            "hasUnknowns", pb false; "nextAction", pt ""; "isValid", pb false; "isInvalid", pb false; "isLoading", pb true; "errorCode", pt ""; "errorText", pt "" ]
                    | Invalid err ->
                        common "invalid" "Refused: failed contract validation"
                        @ [ "title", pt ("Directory " + (if e.Id.Length > 40 then e.Id.Substring(0, 40) else e.Id)); "lifecycle", pt "—"; "lifecycleTone", pt "critical"
                            "version", pn 0L; "updated", pt "—"; "updatedSort", pt ""; "hypotheses", pn 0L; "evidence", pn 0L; "contradictions", pn 0L; "unknowns", pn 0L
                            "hasContradictions", pb false; "hasUnknowns", pb false; "nextAction", pt ""; "isValid", pb false; "isInvalid", pb true; "isLoading", pb false
                            "errorCode", pt (err.Code + (err.DomainCode |> Option.map (fun d -> " / " + d) |> Option.defaultValue ""))
                            "errorText", pt (if err.Detail.Length > 240 then err.Detail.Substring(0, 240) + "…" else err.Detail) ]
                    | Unreadable f ->
                        common "invalid" "Could not be read from GitHub"
                        @ [ "title", pt ("Directory " + e.Id); "lifecycle", pt "—"; "lifecycleTone", pt "critical"; "version", pn 0L; "updated", pt "—"; "updatedSort", pt ""
                            "hypotheses", pn 0L; "evidence", pn 0L; "contradictions", pn 0L; "unknowns", pn 0L; "hasContradictions", pb false; "hasUnknowns", pb false
                            "nextAction", pt ""; "isValid", pb false; "isInvalid", pb true; "isLoading", pb false; "errorCode", pt "PROVIDER"; "errorText", pt (GitHub.describe f) ])
        let key (row: (string * ViewPrimitive) list) name = row |> List.tryFind (fun (k, _) -> k = name) |> Option.map snd
        let sorted =
            match app.SortBy with
            | "title" -> rows |> List.sortBy (fun r -> match key r "title" with Some(ViewPrimitive.Text s) -> s.ToLowerInvariant() | _ -> "")
            | "lifecycle" -> rows |> List.sortBy (fun r -> match key r "lifecycle" with Some(ViewPrimitive.Text s) -> s | _ -> "")
            | _ -> rows |> List.sortByDescending (fun r -> match key r "updatedSort" with Some(ViewPrimitive.Text s) -> s | _ -> "")
        let live = App.live app
        let createGate =
            match live with
            | Some l -> Session.writeAllowed l
            | None -> Error "Connect a data repository first."
        let valid = rows |> List.filter (fun r -> key r "isValid" = Some(ViewPrimitive.Flag true)) |> List.length
        let invalid = rows |> List.filter (fun r -> key r "isInvalid" = Some(ViewPrimitive.Flag true)) |> List.length
        [ "investigations", items sorted
          "dashCount", n (int64 rows.Length)
          "dashValid", n (int64 valid)
          "dashInvalid", n (int64 invalid)
          "dashHasInvalid", b (invalid > 0)
          "dashEmpty", b (live.IsSome && rows.IsEmpty && not (live |> Option.exists (fun l -> l.Refreshing)))
          "dashSummary",
          t (
              match live with
              | None -> "No investigations: no data repository is connected."
              | Some l when rows.IsEmpty -> "This workspace has no investigations yet (" + Target.rootLabel l.Target + ")."
              | Some _ -> string rows.Length + " investigation directories: " + string valid + " verified, " + string invalid + " refused."
          )
          "canCreate", b (Result.isOk createGate)
          "createDisabled", b (Result.isError createGate)
          "createReason", t (match createGate with Ok() -> "" | Error r -> r)
          "targetLabel", t (match live with Some l -> Target.identity l.Target | None -> "")
          "accessLabel",
          t (
              match live with
              | Some l when not (Target.writable l.Target) -> "Read-only: frozen fixtures"
              | Some l when l.Access.CanPush -> "Read and write (repository role); writes go to " + Target.rootLabel l.Target
              | Some _ -> "Read-only: the credential's user cannot push"
              | None -> ""
          )
          "headLabel", t (match live with Some l -> short l.Head.Commit | None -> "")
          "sortBy", t app.SortBy ]

    // ------------------------------------------------------------------------------------------------------------
    // The open investigation
    // ------------------------------------------------------------------------------------------------------------

    let private entityHref app id tab (g: Guid) = href app (InvestigationRoute(id, tab, Some(Ids.text g)))

    let private evidenceText (e: Evidence) = match e.Content with TextEvidence x -> x.Value | ArtifactEvidence u -> u

    /// Artifact URLs are untrusted repository content: only absolute http(s) URLs are offered as links (Limen also
    /// refuses other schemes on href bindings).
    let private safeUrl (u: string) =
        match Uri.TryCreate(u, UriKind.Absolute) with
        | true, NonNull uri when uri.Scheme = Uri.UriSchemeHttps || uri.Scheme = Uri.UriSchemeHttp -> uri.AbsoluteUri
        | _ -> ""

    let private eventSummary (inv: Investigation) (ev: Event) =
        let hyp (id: HypothesisId) = inv.Hypotheses |> Map.tryFind id |> Option.map (fun h -> h.Statement.Value) |> Option.defaultValue (Labels.shortId id.Value)
        match ev.Body with
        | InvestigationStarted(title, _, _) -> "Started \"" + title.Value + "\""
        | LifecycleChanged(f, tt, _, reason) -> Labels.lifecycle f + " → " + Labels.lifecycle tt + (reason |> Option.map (fun r -> ": " + r) |> Option.defaultValue "")
        | ObservationRecorded(_, o, _) -> "Observation: " + o.Value
        | HypothesisRecorded(_, s, _) -> "Hypothesis: " + s.Value
        | HypothesisTransitioned(id, c) -> "Hypothesis " + Labels.hypothesis c.From + " → " + Labels.hypothesis c.To + ": " + hyp id + " — " + c.Reason.Value
        | EvidenceAdmitted(_, content, cat, source, _) ->
            let text = match content with TextEvidence x -> x.Value | ArtifactEvidence u -> u
            Labels.category cat + " evidence from " + source.Value + ": " + text
        | EvidenceSuperseded(_, _, reason) -> "Evidence superseded: " + reason.Value
        | EvidenceVerificationAssessed(_, s) -> "Evidence assessed as " + Labels.verificationStatus s
        | EvidenceRelated(_, _, h, k, _) -> "Evidence " + (Labels.relationship k).ToLowerInvariant() + " " + hyp h
        | UnknownIdentified(_, q, m) -> Labels.materiality m + " unknown: " + q.Value
        | UnknownTransitioned(_, s, basis) -> "Unknown → " + Labels.unknown s + ": " + basis.Value
        | AssumptionRecorded(_, p, m) -> Labels.materiality m + " assumption: " + p.Value
        | AssumptionTransitioned(_, s) -> "Assumption → " + Labels.assumption s
        | ContradictionRecorded(_, s, _, m) -> Labels.materiality m + " contradiction: " + s.Value
        | ContradictionTransitioned(_, s, basis) -> "Contradiction → " + Labels.contradiction s + ": " + basis.Value
        | PredictionRecorded(_, h, s, st, tm) -> Labels.timing tm + " " + (Labels.strength st).ToLowerInvariant() + " prediction for " + hyp h + ": " + s.Value
        | ActionRecorded(_, k, p, _) -> Labels.actionKind k + " proposed: " + p.Value
        | ActionTransitioned(_, s) -> "Action → " + Labels.action s
        | FindingRecorded(_, _, r, _) -> "Finding: " + r.Value
        | NegativeKnowledgeEstablished(_, s, scope, _) -> "Established not the case (" + scope.Value + "): " + s.Value
        | CausalAssertionRecorded(_, o, members, _) -> "Cause proposed for \"" + o.Value + "\" with " + string members.Length + " member(s)"
        | CausalAssertionConfirmed(_, policy) -> "Cause confirmed under policy " + policy
        | CausalAssertionReopened(_, r) -> "Cause reopened: " + r.Value
        | CorrectiveActionRecorded(_, k, d, _) -> Labels.correctionKind k + " proposed: " + d.Value
        | CorrectiveActionTransitioned(_, s) -> "Corrective action → " + Labels.correction s
        | CriterionRecorded(_, _, e, _) -> "Verification criterion: " + e.Value
        | VerificationRecorded(_, _, o, _) -> "Verification " + (Labels.outcome o).ToLowerInvariant()
        | ConclusionRecorded(_, s, st, _, _) -> Labels.conclusion st + ": " + s.Value
        | ProposalRecorded(_, k, x, _) -> Labels.proposalKind k + " proposal: " + x.Value
        | ProposalWasAccepted _ -> "Proposal accepted"
        | ProposalWasRejected(_, r) -> "Proposal rejected: " + r.Value

    let private eventCategory (ev: Event) =
        match ev.Body with
        | InvestigationStarted _ | LifecycleChanged _ -> "lifecycle"
        | ObservationRecorded _ -> "observation"
        | HypothesisRecorded _ | HypothesisTransitioned _ | PredictionRecorded _ -> "hypothesis"
        | EvidenceAdmitted _ | EvidenceSuperseded _ | EvidenceVerificationAssessed _ | EvidenceRelated _ | NegativeKnowledgeEstablished _ -> "evidence"
        | UnknownIdentified _ | UnknownTransitioned _ | AssumptionRecorded _ | AssumptionTransitioned _ | ContradictionRecorded _ | ContradictionTransitioned _ -> "question"
        | ActionRecorded _ | ActionTransitioned _ | FindingRecorded _ -> "action"
        | CausalAssertionRecorded _ | CausalAssertionConfirmed _ | CausalAssertionReopened _ | CorrectiveActionRecorded _ | CorrectiveActionTransitioned _
        | CriterionRecorded _ | VerificationRecorded _ | ConclusionRecorded _ -> "cause"
        | ProposalRecorded _ | ProposalWasAccepted _ | ProposalWasRejected _ -> "proposal"

    let private actorLabel (a: Actor) =
        match a.Kind with
        | Human -> a.Id
        | System -> "system"
        | Agent(provider, model) -> a.Id + " (agent: " + provider + " " + model + ")"

    /// Find a selected entity anywhere in the investigation.
    let private findEntity (inv: Investigation) (g: Guid) : (EntityKindView * string * string * (string * string) list) option =
        let meta (p: Provenance) = [ "Recorded by", actorLabel p.Actor; "Recorded at", time p.RecordedAt ]
        let tryIn (m: Map<Id<'T>, 'E>) = m |> Map.tryFind (Id.create g)
        match tryIn inv.Hypotheses with
        | Some h ->
            Some(HypothesisView, "Hypothesis", h.Statement.Value,
                 [ "State", Labels.hypothesis h.State; "Mechanism", Labels.optText h.Mechanism |> fun s -> if s = "" then "Not stated" else s
                   "State changes", string h.History.Length ] @ meta h.Provenance)
        | None ->
        match tryIn inv.Evidence with
        | Some e ->
            Some(EvidenceView, "Evidence", evidenceText e,
                 [ "Category", Labels.category e.Category; "Source", e.Source.Value; "Captured", optTime e.CapturedAt
                   "Verification", Labels.verificationStatus e.Verification
                   "Superseded by", (e.SupersededBy |> Option.map (fun s -> Labels.shortId s.Value) |> Option.defaultValue "Not superseded")
                   "Admitted at version", string e.AdmittedAt
                   "Content kind", (match e.Content with TextEvidence _ -> "Verbatim text" | ArtifactEvidence _ -> "Artifact reference (URL)") ] @ meta e.Provenance)
        | None ->
        match tryIn inv.Unknowns with
        | Some u -> Some(UnknownView, "Unknown", u.Question.Value, [ "State", Labels.unknown u.State; "Materiality", Labels.materiality u.Materiality; "Basis", Labels.optText u.Basis ] @ meta u.Provenance)
        | None ->
        match tryIn inv.Assumptions with
        | Some a -> Some(AssumptionView, "Assumption", a.Proposition.Value, [ "State", Labels.assumption a.State; "Materiality", Labels.materiality a.Materiality ] @ meta a.Provenance)
        | None ->
        match tryIn inv.Contradictions with
        | Some c ->
            Some(ContradictionView, "Contradiction", c.Statement.Value,
                 [ "State", Labels.contradiction c.State; "Materiality", Labels.materiality c.Materiality; "Basis", Labels.optText c.Basis
                   "Conflicting evidence", string c.Evidence.Length ] @ meta c.Provenance)
        | None ->
        match tryIn inv.Actions with
        | Some a -> Some(ActionView, "Action", a.Purpose.Value, [ "Kind", Labels.actionKind a.Kind; "State", Labels.action a.State; "Hypotheses tested", string a.Targets.Length ] @ meta a.Provenance)
        | None ->
        match tryIn inv.Causes with
        | Some c ->
            Some(CauseView, "Causal assertion", c.Outcome.Value,
                 [ "State", Labels.cause c.State; "Mechanism", Labels.optText c.Mechanism
                   "Members", (c.Members |> List.map (fun m -> Labels.role m.Role + ": " + (inv.Hypotheses |> Map.tryFind m.Hypothesis |> Option.map (fun h -> h.Statement.Value) |> Option.defaultValue "?")) |> String.concat "; ") ]
                 @ meta c.Provenance)
        | None ->
        match tryIn inv.CorrectiveActions with
        | Some c -> Some(CorrectionView, "Corrective action", c.Description.Value, [ "Kind", Labels.correctionKind c.Kind; "State", Labels.correction c.State ] @ meta c.Provenance)
        | None ->
        match tryIn inv.Criteria with
        | Some c -> Some(CriterionView, "Verification criterion", c.ExpectedOutcome.Value, [ "Method", Labels.optText c.Method ] @ meta c.Provenance)
        | None ->
        match tryIn inv.Proposals with
        | Some p ->
            Some(ProposalView, "Proposal (never evidence)", p.Text.Value,
                 [ "Kind", Labels.proposalKind p.Kind; "State", Labels.proposalState p.State; "Rationale", Labels.optText p.Rationale; "Decision", Labels.optText p.Decision ] @ meta p.Provenance)
        | None ->
        match tryIn inv.Observations with
        | Some o -> Some(ObservationView, "Observation", o.Original.Value, [ "Observed at", optTime o.ObservedAt ] @ meta o.Provenance)
        | None ->
        match tryIn inv.Predictions with
        | Some p -> Some(PredictionView, "Prediction", p.Statement.Value, [ "Strength", Labels.strength p.Strength; "Timing", Labels.timing p.Timing ] @ meta p.Provenance)
        | None ->
        match tryIn inv.Findings with
        | Some f -> Some(FindingView, "Finding", f.OriginalResult.Value, [ "Interpretation", Labels.optText f.Interpretation ] @ meta f.Provenance)
        | None ->
        match tryIn inv.Verifications with
        | Some v -> Some(VerificationView, "Verification", Labels.outcome v.Outcome, [ "Evidence cited", string v.Evidence.Length; "Recorded at version", string v.Sequence ] @ meta v.Provenance)
        | None ->
        match tryIn inv.Conclusions with
        | Some c -> Some(ConclusionView, "Conclusion", c.Statement.Value, [ "Strength", Labels.conclusion c.Strength; "Residual uncertainty", Labels.optText c.ResidualUncertainty ] @ meta c.Provenance)
        | None ->
        match tryIn inv.NegativeKnowledge with
        | Some k -> Some(NegativeView, "Negative knowledge", k.Statement.Value, [ "Scope", k.Scope.Value ] @ meta k.Provenance)
        | None -> None

    let private links (app: App) (id: string) (inv: Investigation) (g: Guid) : (string * string * string) list =
        let h (x: HypothesisId) = x.Value, "Hypothesis: " + (inv.Hypotheses |> Map.tryFind x |> Option.map (fun v -> v.Statement.Value) |> Option.defaultValue "?"), Hypotheses
        let e (x: EvidenceId) = x.Value, "Evidence: " + (inv.Evidence |> Map.tryFind x |> Option.map evidenceText |> Option.defaultValue "?"), EvidenceTab
        let rels =
            inv.Relationships
            |> Map.toList
            |> List.map snd
            |> List.collect (fun r ->
                if r.Evidence.Value = g then [ (let a, l, tb = h r.Hypothesis in a, Labels.relationship r.Kind + " → " + l, tb) ]
                elif r.Hypothesis.Value = g then [ (let a, l, tb = e r.Evidence in a, Labels.relationship r.Kind + " ← " + l, tb) ]
                else [])
        let fromContradictions =
            inv.Contradictions |> Map.toList |> List.map snd |> List.filter (fun c -> c.Evidence |> List.exists (fun x -> x.Value = g))
            |> List.map (fun c -> c.Id.Value, "In contradiction: " + c.Statement.Value, Questions)
        let contradictionEvidence =
            match inv.Contradictions |> Map.tryFind (Id.create g) with
            | Some c -> c.Evidence |> List.map e
            | None -> []
        let causeMembers =
            match inv.Causes |> Map.tryFind (Id.create g) with
            | Some c -> c.Members |> List.map (fun m -> let a, l, tb = h m.Hypothesis in a, Labels.role m.Role + ": " + l, tb)
            | None -> []
        let predictions = inv.Predictions |> Map.toList |> List.map snd |> List.filter (fun p -> p.Hypothesis.Value = g) |> List.map (fun p -> p.Id.Value, "Prediction: " + p.Statement.Value, Hypotheses)
        let criteria = inv.Criteria |> Map.toList |> List.map snd |> List.filter (fun c -> c.CorrectiveAction.Value = g) |> List.map (fun c -> c.Id.Value, "Criterion: " + c.ExpectedOutcome.Value, Causes)
        let findings = inv.Findings |> Map.toList |> List.map snd |> List.filter (fun f -> f.Action.Value = g) |> List.map (fun f -> f.Id.Value, "Finding: " + f.OriginalResult.Value, Questions)
        rels @ fromContradictions @ contradictionEvidence @ causeMembers @ predictions @ criteria @ findings
        |> List.map (fun (x, label, tab) -> Ids.text x, label, entityHref app id tab x)

    let private investigation (app: App) =
        let empty =
            [ "invTitle", t ""; "invId", t ""; "invLifecycle", t ""; "invLifecycleTone", t "neutral"; "invVersion", n 0L; "invCreated", t ""; "invUpdated", t ""
              "invLocal", b false; "invReadOnly", b true; "invReadOnlyReason", t ""; "invNext", t ""; "invEventCount", n 0L
              "actionsBlocked", b true; "actionsOpen", b false
              "invLoading", b false; "invMissing", b false; "invMissingText", t ""; "hasInvestigation", b false
              "tabs", items []; "invActions", items []; "observations", items []; "hypotheses", items []; "evidence", items []
              "relationships", items []; "unknowns", items []; "assumptions", items []; "contradictions", items []; "predictions", items []
              "actions", items []; "findings", items []; "negativeKnowledge", items []; "causes", items []; "corrections", items []
              "criteria", items []; "verifications", items []; "conclusions", items []; "proposals", items []; "timeline", items []
              "timelineFilter", t app.TimelineFilter; "timelineCount", n 0L
              "hasSelection", b false; "selKind", t ""; "selTitle", t ""; "selBody", t ""; "selMeta", items []; "selActions", items []; "selLinks", items []
              "selCloseHref", t "#/"; "selUrl", t ""; "hasSelUrl", b false
              "openUnknownCount", n 0L; "openContradictionCount", n 0L; "blockingCount", n 0L; "hasBlockers", b false
              "currentConclusion", t ""; "hasConclusion", b false; "hasCauses", b false; "reportGenerated", t "" ]
            |> Map.ofList
        match app.Route, App.live app with
        | InvestigationRoute(id, tab, entity), Some live ->
            match Session.displayed live id with
            | None ->
                let loading = Session.entry live id |> Option.exists (fun e -> e.Status = Fetching) || live.Refreshing
                let missing =
                    match Session.entry live id with
                    | Some { Status = Invalid err } -> "This investigation failed contract validation and is not shown (" + err.Code + "): " + err.Detail
                    | Some { Status = Unreadable f } -> GitHub.describe f
                    | _ when loading -> ""
                    | _ -> "No investigation with this identifier exists in the connected workspace."
                empty |> Map.add "invLoading" (b loading) |> Map.add "invMissing" (b (not loading)) |> Map.add "invMissingText" (t missing)
            | Some(loaded, local) ->
                let inv = loaded.State
                let now = app.Context.Now
                let gate = Session.writeAllowed live
                let eh tabOf (g: Guid) = entityHref app id tabOf g
                let selected = entity |> Option.bind (fun e -> match Guid.TryParse e with true, g -> Some g | _ -> None)
                let isSel (g: Guid) = selected = Some g
                let row (g: Guid) tabOf extra = [ "id", pt (Ids.text g); "href", pt (eh tabOf g); "selected", pb (isSel g) ] @ extra
                let byProv (f: 'E -> Provenance) (xs: 'E list) = xs |> List.sortBy (fun x -> (f x).RecordedAt)
                let values (m: Map<'K, 'V>) = m |> Map.toList |> List.map snd
                let openUnknowns = values inv.Unknowns |> List.filter isOpenUnknown
                let openContradictions = values inv.Contradictions |> List.filter isOpenContradiction
                let blocking =
                    (openUnknowns |> List.filter (fun u -> u.Materiality = Blocking) |> List.length)
                    + (openContradictions |> List.filter (fun c -> c.Materiality = Blocking) |> List.length)
                let evidenceSupport (h: Hypothesis) kind =
                    values inv.Relationships |> List.filter (fun r -> r.Hypothesis = h.Id && r.Kind = kind) |> List.length
                let current = values inv.Conclusions |> List.sortByDescending (fun c -> c.Sequence) |> List.tryHead
                let filter = app.TimelineFilter
                let timeline =
                    loaded.Events
                    |> List.filter (fun ev -> filter = "all" || eventCategory ev = filter)
                    |> List.map (fun ev ->
                        [ "id", pt (string ev.Sequence); "seq", pn ev.Sequence; "type", pt (CaseNames.eventType ev.Body); "category", pt (eventCategory ev)
                          "actor", pt (actorLabel ev.Actor); "at", pt (time ev.At); "summary", pt (eventSummary inv ev)
                          "isAgent", pb (match ev.Actor.Kind with Agent _ -> true | _ -> false) ])
                let selection = selected |> Option.bind (fun g -> findEntity inv g |> Option.map (fun x -> g, x))
                let selActions =
                    match selection with
                    | Some(g, (kind, _, _, _)) ->
                        Capabilities.entityCommands kind
                        |> List.map (fun k -> Capabilities.judge gate now inv k (Some g))
                        |> List.map (fun a -> [ "id", pt (CommandKind.key a.Kind); "label", pt (CommandKind.label a.Kind); "enabled", pb a.Enabled; "disabled", pb (not a.Enabled); "reason", pt a.Reason ])
                    | None -> []
                let selUrl =
                    match selection with
                    | Some(g, (EvidenceView, _, body, _)) -> (match inv.Evidence |> Map.tryFind (Id.create g) with Some { Content = ArtifactEvidence u } -> safeUrl u | _ -> "")
                    | _ -> ""
                [ "invTitle", t inv.Title.Value
                  "invId", t id
                  "invLifecycle", t (Labels.lifecycle inv.Lifecycle)
                  "invLifecycleTone", t (toneKey (lifecycleTone inv.Lifecycle))
                  "invVersion", n inv.Version
                  "invCreated", t (time inv.CreatedAt)
                  "invUpdated", t (time (List.last loaded.Events).At)
                  "invLocal", b local
                  "invReadOnly", b (Result.isError gate)
                  "actionsBlocked", b (Result.isError gate)
                  "actionsOpen", b (Result.isOk gate)
                  "invReadOnlyReason", t (match gate with Ok() -> "" | Error r -> r)
                  "invNext", t (Capabilities.nextLifecycle now inv)
                  "invEventCount", n (int64 loaded.Events.Length)
                  "invLoading", b false
                  "invMissing", b false
                  "invMissingText", t ""
                  "hasInvestigation", b true
                  "tabs", items (Tab.all |> List.map (fun tb -> [ "id", pt (Tab.key tb); "label", pt (Tab.label tb); "current", pb (tb = tab); "href", pt (href app (InvestigationRoute(id, tb, None))) ]))
                  "invActions",
                  items (
                      Capabilities.investigationCommands
                      |> List.map (fun k -> Capabilities.judge gate now inv k None)
                      |> List.map (fun a ->
                          let group = match a.Kind with Activate | AwaitEvidence | AwaitVerification | Resolve | Close | ReopenInv -> "lifecycle" | _ -> "record"
                          [ "id", pt (CommandKind.key a.Kind); "label", pt (CommandKind.label a.Kind); "enabled", pb a.Enabled; "disabled", pb (not a.Enabled)
                            "reason", pt a.Reason; "group", pt group; "isLifecycle", pb (group = "lifecycle") ])
                  )
                  "observations", items (values inv.Observations |> byProv (fun o -> o.Provenance) |> List.map (fun o -> row o.Id.Value Overview [ "text", pt o.Original.Value; "at", pt (optTime o.ObservedAt); "recorded", pt (time o.Provenance.RecordedAt) ]))
                  "hypotheses",
                  items (
                      values inv.Hypotheses
                      |> byProv (fun h -> h.Provenance)
                      |> List.map (fun h ->
                          row h.Id.Value Hypotheses
                              [ "statement", pt h.Statement.Value; "mechanism", pt (Labels.optText h.Mechanism); "state", pt (Labels.hypothesis h.State)
                                "stateKey", pt ((sprintf "%A" h.State).ToLowerInvariant()); "tone", pt (toneKey (hypothesisTone h.State))
                                "supports", pn (int64 (evidenceSupport h Supports)); "contradicts", pn (int64 (evidenceSupport h Contradicts))
                                "changes", pn (int64 h.History.Length); "isFalsified", pb (h.State = Falsified) ])
                  )
                  "evidence",
                  items (
                      values inv.Evidence
                      |> List.sortBy (fun e -> e.AdmittedAt)
                      |> List.map (fun e ->
                          row e.Id.Value EvidenceTab
                              [ "content", pt (evidenceText e); "category", pt (Labels.category e.Category); "source", pt e.Source.Value
                                "captured", pt (optTime e.CapturedAt); "verification", pt (Labels.verificationStatus e.Verification)
                                "isArtifact", pb (match e.Content with ArtifactEvidence _ -> true | _ -> false)
                                "superseded", pb e.SupersededBy.IsSome; "version", pn e.AdmittedAt ])
                  )
                  "relationships",
                  items (
                      values inv.Relationships
                      |> List.sortBy (fun r -> Ids.text r.Id.Value)
                      |> List.map (fun r ->
                          let ev = inv.Evidence |> Map.tryFind r.Evidence |> Option.map evidenceText |> Option.defaultValue "?"
                          let hy = inv.Hypotheses |> Map.tryFind r.Hypothesis |> Option.map (fun h -> h.Statement.Value) |> Option.defaultValue "?"
                          [ "id", pt (Ids.text r.Id.Value); "evidence", pt ev; "hypothesis", pt hy; "kind", pt (Labels.relationship r.Kind)
                            "kindKey", pt ((sprintf "%A" r.Kind).ToLowerInvariant()); "rationale", pt (Labels.optText r.Rationale)
                            "evidenceHref", pt (eh EvidenceTab r.Evidence.Value); "hypothesisHref", pt (eh Hypotheses r.Hypothesis.Value)
                            "isContradicts", pb (r.Kind = Contradicts) ])
                  )
                  "unknowns", items (values inv.Unknowns |> byProv (fun u -> u.Provenance) |> List.map (fun u -> row u.Id.Value Questions [ "question", pt u.Question.Value; "state", pt (Labels.unknown u.State); "materiality", pt (Labels.materiality u.Materiality); "isOpen", pb (isOpenUnknown u); "isBlocking", pb (u.Materiality = Blocking && isOpenUnknown u) ]))
                  "assumptions", items (values inv.Assumptions |> byProv (fun a -> a.Provenance) |> List.map (fun a -> row a.Id.Value Questions [ "proposition", pt a.Proposition.Value; "state", pt (Labels.assumption a.State); "materiality", pt (Labels.materiality a.Materiality); "isUnverified", pb (a.State = ExplicitUnverified || a.State = ProvisionallyAccepted) ]))
                  "contradictions", items (values inv.Contradictions |> byProv (fun c -> c.Provenance) |> List.map (fun c -> row c.Id.Value Questions [ "statement", pt c.Statement.Value; "state", pt (Labels.contradiction c.State); "materiality", pt (Labels.materiality c.Materiality); "isOpen", pb (isOpenContradiction c); "evidenceCount", pn (int64 c.Evidence.Length) ]))
                  "predictions", items (values inv.Predictions |> byProv (fun p -> p.Provenance) |> List.map (fun p -> row p.Id.Value Hypotheses [ "statement", pt p.Statement.Value; "strength", pt (Labels.strength p.Strength); "timing", pt (Labels.timing p.Timing); "hypothesis", pt (inv.Hypotheses |> Map.tryFind p.Hypothesis |> Option.map (fun h -> h.Statement.Value) |> Option.defaultValue "?") ]))
                  "actions", items (values inv.Actions |> byProv (fun a -> a.Provenance) |> List.map (fun a -> row a.Id.Value Questions [ "purpose", pt a.Purpose.Value; "kind", pt (Labels.actionKind a.Kind); "state", pt (Labels.action a.State) ]))
                  "findings", items (values inv.Findings |> byProv (fun f -> f.Provenance) |> List.map (fun f -> row f.Id.Value Questions [ "result", pt f.OriginalResult.Value; "interpretation", pt (Labels.optText f.Interpretation) ]))
                  "negativeKnowledge", items (values inv.NegativeKnowledge |> byProv (fun k -> k.Provenance) |> List.map (fun k -> row k.Id.Value EvidenceTab [ "statement", pt k.Statement.Value; "scope", pt k.Scope.Value; "basis", pn (int64 k.Basis.Length) ]))
                  "causes", items (values inv.Causes |> byProv (fun c -> c.Provenance) |> List.map (fun c -> row c.Id.Value Causes [ "outcome", pt c.Outcome.Value; "state", pt (Labels.cause c.State); "isConfirmed", pb (c.State = CauseConfirmed); "members", pt (c.Members |> List.map (fun m -> Labels.role m.Role + ": " + (inv.Hypotheses |> Map.tryFind m.Hypothesis |> Option.map (fun h -> h.Statement.Value) |> Option.defaultValue "?")) |> String.concat " · "); "mechanism", pt (Labels.optText c.Mechanism) ]))
                  "corrections", items (values inv.CorrectiveActions |> byProv (fun c -> c.Provenance) |> List.map (fun c -> row c.Id.Value Causes [ "description", pt c.Description.Value; "kind", pt (Labels.correctionKind c.Kind); "state", pt (Labels.correction c.State) ]))
                  "criteria", items (values inv.Criteria |> byProv (fun c -> c.Provenance) |> List.map (fun c -> row c.Id.Value Causes [ "expected", pt c.ExpectedOutcome.Value; "method", pt (Labels.optText c.Method) ]))
                  "verifications", items (values inv.Verifications |> List.sortBy (fun v -> v.Sequence) |> List.map (fun v -> row v.Id.Value Causes [ "outcome", pt (Labels.outcome v.Outcome); "isSatisfied", pb (v.Outcome = Satisfied); "evidence", pn (int64 v.Evidence.Length); "version", pn v.Sequence ]))
                  "conclusions", items (values inv.Conclusions |> List.sortBy (fun c -> c.Sequence) |> List.map (fun c -> row c.Id.Value Causes [ "statement", pt c.Statement.Value; "strength", pt (Labels.conclusion c.Strength); "residual", pt (Labels.optText c.ResidualUncertainty); "version", pn c.Sequence; "isCurrent", pb (Some c = current) ]))
                  "proposals", items (values inv.Proposals |> byProv (fun p -> p.Provenance) |> List.map (fun p -> row p.Id.Value Questions [ "text", pt p.Text.Value; "kind", pt (Labels.proposalKind p.Kind); "state", pt (Labels.proposalState p.State); "actor", pt (actorLabel p.Provenance.Actor) ]))
                  "timeline", items timeline
                  "timelineFilter", t filter
                  "timelineCount", n (int64 timeline.Length)
                  "hasSelection", b selection.IsSome
                  "selKind", t (match selection with Some(_, (_, k, _, _)) -> k | None -> "")
                  "selTitle", t (match selection with Some(_, (_, k, _, _)) -> k | None -> "")
                  "selBody", t (match selection with Some(_, (_, _, body, _)) -> body | None -> "")
                  "selMeta", items (match selection with Some(_, (_, _, _, meta)) -> meta |> List.mapi (fun i (k, v) -> [ "id", pt (string i); "label", pt k; "value", pt (if v = "" then "Not stated" else v) ]) | None -> [])
                  "selActions", items selActions
                  "selLinks", items (match selection with Some(g, _) -> links app id inv g |> List.map (fun (x, l, h) -> [ "id", pt x; "label", pt l; "href", pt h ]) | None -> [])
                  "selCloseHref", t (href app (InvestigationRoute(id, tab, None)))
                  "selUrl", t selUrl
                  "hasSelUrl", b (selUrl <> "")
                  "openUnknownCount", n (int64 openUnknowns.Length)
                  "openContradictionCount", n (int64 openContradictions.Length)
                  "blockingCount", n (int64 blocking)
                  "hasBlockers", b (blocking > 0)
                  "currentConclusion", t (current |> Option.map (fun c -> Labels.conclusion c.Strength + ": " + c.Statement.Value) |> Option.defaultValue "No conclusion has been recorded.")
                  "hasConclusion", b current.IsSome
                  "hasCauses", b (not inv.Causes.IsEmpty)
                  "reportGenerated", t (time now) ]
                |> Map.ofList
                |> fun m -> Map.fold (fun acc k v -> if Map.containsKey k acc then acc else Map.add k v acc) m empty
        | _ -> empty

    // ------------------------------------------------------------------------------------------------------------
    // The command dialog
    // ------------------------------------------------------------------------------------------------------------

    let private dialog (app: App) =
        let slotKeys = [ TextA, "textA"; TextB, "textB"; EnumA, "enumA"; EnumB, "enumB"; RefA, "refA"; RefsB, "refsB" ]
        match app.Dialog with
        | None ->
            [ "dialogOpen", b false; "dialogTitle", t ""; "dialogError", t ""; "hasDialogError", b false; "dialogConfirm", t ""; "hasConfirm", b false
              "dialogConfirming", b false; "dialogSubmitLabel", t "Submit"; "dialogTarget", t "" ]
            @ (slotKeys |> List.collect (fun (_, k) -> [ k + "Label", t ""; k + "Hidden", b true; k + "Help", t ""; k + "Required", b false; k + "Value", t "" ]))
            @ [ "enumAOptions", items []; "enumBOptions", items []; "refAOptions", items []; "refsBOptions", items [] ]
        | Some d ->
            let inv =
                match app.Route, App.live app with
                | InvestigationRoute(id, _, _), Some l -> Session.loaded l id |> Option.map (fun (_, x) -> x.State)
                | _ -> None
            let form = CommandForms.describe inv d.Kind d.Target
            let fieldOf slot = form.Fields |> List.tryFind (fun f -> f.Slot = slot)
            let options slot = form.Options |> Map.tryFind slot |> Option.defaultValue []
            let value slot = FormDraft.get slot d.Draft
            let selectOptions slot =
                ("", "Choose…") :: options slot
                |> List.map (fun (v, l) -> [ "id", pt (if v = "" then "_none" else v); "value", pt v; "label", pt l; "selected", pb (value slot = v) ])
            let targetLabel =
                match inv, d.Target with
                | Some i, Some g -> findEntity i g |> Option.map (fun (_, k, body, _) -> k + ": " + body) |> Option.defaultValue ""
                | _ -> ""
            [ "dialogOpen", b true
              "dialogTitle", t form.Title
              "dialogError", t (d.Error |> Option.defaultValue "")
              "hasDialogError", b d.Error.IsSome
              "dialogConfirm", t (form.Confirm |> Option.defaultValue "")
              "hasConfirm", b (form.Confirm.IsSome && d.Confirming)
              "dialogConfirming", b d.Confirming
              "dialogSubmitLabel", t (if d.Confirming then "Confirm and save" else if form.Fields.IsEmpty then form.Title else "Save to GitHub")
              "dialogTarget", t targetLabel ]
            @ (slotKeys
               |> List.collect (fun (slot, k) ->
                   match fieldOf slot with
                   | Some f -> [ k + "Label", t (f.Label + (if f.Required then "" else " (optional)")); k + "Hidden", b false; k + "Help", t f.Help; k + "Required", b f.Required; k + "Value", t (value slot) ]
                   | None -> [ k + "Label", t ""; k + "Hidden", b true; k + "Help", t ""; k + "Required", b false; k + "Value", t "" ]))
            @ [ "enumAOptions", items (selectOptions EnumA)
                "enumBOptions", items (selectOptions EnumB)
                "refAOptions", items (selectOptions RefA)
                "refsBOptions", items (options RefsB |> List.map (fun (v, l) -> [ "id", pt v; "label", pt l; "checked", pb (d.Draft.Selected.Contains v) ])) ]

    // ------------------------------------------------------------------------------------------------------------
    // Whole projection
    // ------------------------------------------------------------------------------------------------------------

    let project (app: App) : ViewState =
        let route =
            match app.Route with
            | Home -> "home"
            | InvestigationRoute _ -> "investigation"
            | NotFoundRoute _ -> "notfound"
        let tab = match app.Route with InvestigationRoute(_, tb, _) -> Tab.key tb | _ -> ""
        let inv = investigation app
        let title =
            match app.Route, inv |> Map.tryFind "invTitle" with
            | InvestigationRoute(_, tb, _), Some(ViewValue.Text s) when s <> "" -> s + " · " + Tab.label tb + " · Causa"
            | NotFoundRoute _, _ -> "Not found · Causa"
            | _ -> "Causa · Investigations"
        [ "appStatus", t (match app.Boot with Running -> "ready" | Starting -> "starting" | HandshakeRefused _ -> "incompatible")
          "pageTitle", t title
          "route", t route
          "isHome", b (route = "home")
          "isInvestigation", b (route = "investigation")
          "isNotFound", b (route = "notfound")
          "tabKey", t tab
          "isOverview", b (tab = "overview")
          "isHypotheses", b (tab = "hypotheses")
          "isEvidence", b (tab = "evidence")
          "isQuestions", b (tab = "questions")
          "isCauses", b (tab = "causes")
          "isTimeline", b (tab = "timeline")
          "isReport", b (tab = "report")
          "homeHref", t "#/"
          "hasNotice", b app.Notice.IsSome
          "noticeText", t (app.Notice |> Option.map (fun x -> x.Text) |> Option.defaultValue "")
          "noticeTone", t (app.Notice |> Option.map (fun x -> toneKey x.Tone) |> Option.defaultValue "neutral")
          "contractVersion", t "Causa storage contract 1.0.0"
          "engineVersion", t BuildInfo.version
          "sourceCommit", t BuildInfo.commit ]
        @ connection app
        @ syncView app
        @ dashboard app
        @ (inv |> Map.toList)
        @ dialog app
        |> Map.ofList
