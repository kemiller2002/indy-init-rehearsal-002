namespace Causa.Engine

open System
open Limen.Contract
open Limen.Contract.Core
open Limen.Guest
open IndyInitRootCauseInvestigator.Engine
open Causa.Contract

/// The Limen boundary of the Causa engine. `handle` is the whole application: one pure transition per browser
/// message, returning the next state and the complete response (projection, effects, cancellations, handshake).
/// Entropy and time arrive in the message context; nothing here performs I/O.
[<RequireQualifiedAccess>]
module Shell =
    let requirements = Handshake.coreOnly

    let private httpMethod (m: string) =
        match m with
        | "POST" -> HttpMethod.Post
        | "PATCH" -> HttpMethod.Patch
        | _ -> HttpMethod.Get

    /// The Authorization header is the only place the credential appears; Limen never surfaces request headers in
    /// diagnostics, and the kernel does not interpret them (EXT-DATA-004).
    let private http (token: string) (correlation: string) (request: ApiRequest) : EffectRequest =
        EffectRequest.Http
            { CorrelationId = CorrelationId correlation
              Method = httpMethod request.Method
              // The correlation id is unique per request, so a fresh read can never be answered from a cache.
              Url = GitHub.apiBase + request.Path + (if request.Fresh then (if request.Path.Contains '?' then "&" else "?") + "fresh=" + correlation else "")
              Headers =
                Some(
                    Map.ofList
                        [ "Accept", "application/vnd.github+json"
                          "Authorization", "Bearer " + token
                          "X-GitHub-Api-Version", GitHub.apiVersion
                          if request.Body.IsSome then "Content-Type", "application/json" ]
                )
              Body = request.Body
              TimeoutMs = 30000L
              Response = None
              ResponseHeaders = None
              Credentials = Some HttpCredentials.Omit
              Xsrf = None }

    let private token (app: App) =
        match app.Connection with
        | Connecting(_, t, _, _, _) -> Some t
        | Connected l -> Some l.Token
        | _ -> None

    /// Issue provider requests, correlating each with the question it answers and the write generation that asked
    /// it. An answer is applied only while that exact question is still open (DC-002).
    let private emitGen (generation: int) (ops: (Op * ApiRequest) list) (app: App) : App * EffectRequest list =
        match token app with
        | None -> app, []
        | Some t ->
            ops
            |> List.fold
                (fun (a: App, effects) (op, request) ->
                    let id = "gh-" + string (a.Counter + 1)
                    { a with Counter = a.Counter + 1; Inflight = a.Inflight |> Map.add id (op, generation) }, effects @ [ http t id request ])
                (app, [])

    let private emit ops app = emitGen 0 ops app

    let private navigate (route: Route) (app: App) : App * EffectRequest list =
        let id = "nav-" + string (app.Counter + 1)
        { app with Counter = app.Counter + 1; Route = route },
        [ EffectRequest.Navigation(NavigationEffectRequest.Push(CorrelationId id, app.Base + Route.format route)) ]

    let private outcome (o: EffectOutcome) : Result<string, ProviderFailure> =
        match o with
        | EffectOutcome.Success(status, RawJson body, _) -> GitHub.classify status body
        | EffectOutcome.Failure(HttpFailureReason.Network, _) -> Error Offline
        | EffectOutcome.Failure(_, Some status) -> GitHub.classify status ""
        | EffectOutcome.Failure(reason, None) -> Error(Malformed(sprintf "transport (%A)" reason))
        | EffectOutcome.Cancelled -> Error ProviderFailure.OutcomeUnknown
        | EffectOutcome.OutcomeUnknown _ -> Error ProviderFailure.OutcomeUnknown

    // ------------------------------------------------------------------------------------------------------------
    // Connecting and reloading
    // ------------------------------------------------------------------------------------------------------------

    let connect (app: App) : App * EffectRequest list =
      match app.Connection with
      | Connecting _ | Connected _ -> app, [] // only from Disconnected or Refused (DC-002: events are checked against state)
      | Disconnected | Refused _ ->
        let f k = app.ConnectForm |> Map.tryFind k |> Option.defaultValue ""
        let credential = f "token"
        let form = app.ConnectForm |> Map.remove "token"
        match Target.create (f "owner") (f "repo") (f "branch") (f "root"), String.IsNullOrWhiteSpace credential with
        | Error problems, _ -> { app with ConnectForm = form } |> App.notice Critical (String.Join(" ", problems)), []
        | Ok _, true ->
            { app with ConnectForm = form }
            |> App.notice Critical "A credential is required. Use a short-lived, fine-grained token limited to this repository (Contents: read; add write for a workspace).",
            []
        | Ok target, false ->
            match Schemas.set.Force() with
            | Error e -> { app with ConnectForm = form } |> App.notice Critical ("The embedded Causa v1.0.0 schemas failed to load (" + e.Code + "); the application cannot read data."), []
            | Ok _ ->
                { app with ConnectForm = form; Connection = Connecting(target, credential.Trim(), CheckingRepository, None, None); Notice = None; Inflight = Map.empty }
                |> emit [ OpRepository, GitHub.getRepository target ]

    let disconnect (message: string) (app: App) : App * CorrelationId list =
        let cancelled = app.Inflight |> Map.toList |> List.map (fst >> CorrelationId)
        { app with Connection = Disconnected; Inflight = Map.empty; Dialog = None; Route = Home; ConnectForm = app.ConnectForm |> Map.remove "token" }
        |> App.notice Neutral message,
        cancelled

    let refresh (app: App) : App * EffectRequest list =
        match app.Connection with
        | Connected l when not l.Refreshing && (match l.Sync with InFlight _ -> false | _ -> true) ->
            { app with Connection = Connected { l with Refreshing = true } } |> emit [ OpRef, GitHub.getRef l.Target ]
        | _ -> app, []

    // ------------------------------------------------------------------------------------------------------------
    // Deciding and publishing commands
    // ------------------------------------------------------------------------------------------------------------

    /// Begin publishing. Only from Idle or Held: a write never replaces another write in flight (DC-002).
    let private startWrite (live: Live) (w: Write) (app: App) =
        match live.Sync with
        | InFlight _ -> app |> App.notice Critical "A change is already being saved; nothing else was started.", []
        | Idle | Held _ ->
            let generated = { w with Generation = app.Counter + 1 }
            { app with Connection = Connected { live with Sync = InFlight(generated, CreatingBlobs) } }
            |> emitGen generated.Generation (Session.blobRequests live generated)

    let private refusalText (errors: DomainError list) =
        errors |> List.map (fun e -> e.Message + " (" + e.Code + ")") |> String.concat " "

    /// Decide a command through the domain and, when accepted, build and verify the canonical file set before any
    /// publication (CONTRACT §9.2). A refusal is shown and nothing is written.
    let decide (live: Live) (kind: CommandKind) (target: Guid option) (draft: FormDraft) (currentRoute: Route) (app: App)
        : Result<Write, string> =
        let fresh = Ids.derive app.Context.Seed
        let investigationOf () =
            match currentRoute with
            | InvestigationRoute(id, _, _) -> Session.loaded live id |> Option.map snd
            | _ -> None
        match CommandForms.build kind target draft fresh with
        | Error message -> Error message
        | Ok command ->
            let current = if kind = NewInvestigation then None else investigationOf ()
            let collides = match command with StartInvestigation(id, _, _, _) -> (Session.entry live (Ids.text id.Value)).IsSome | _ -> false
            match kind <> NewInvestigation, current with
            | true, None -> Error "The investigation is not loaded."
            | false, _ when collides -> Error "A directory with this investigation identifier already exists; nothing was written. Try again."
            | _ ->
                let invId =
                    match command, current with
                    | StartInvestigation(id, _, _, _), _ -> id
                    | _, Some l -> l.State.Id
                    | _, None -> Id.create (fresh 1)
                let envelope =
                    { CommandId = fresh 0
                      Investigation = invId
                      ExpectedVersion = current |> Option.map (fun l -> l.State.Version)
                      Actor = Session.actor
                      RequestedAt = Ids.truncate app.Context.Now
                      Command = command }
                match Decide.execute (current |> Option.map (fun l -> l.State)) envelope with
                | Error errors -> Error("The domain refused this command: " + refusalText errors)
                | Ok events ->
                    let built =
                        match Schemas.set.Force(), current with
                        | Error e, _ -> Error e
                        | Ok set, Some l -> Store.append set l l.State.Version events
                        | Ok set, None -> Store.build set events
                    match built with
                    | Error e -> Error("The change cannot be written in the Causa v1.0.0 format: " + e.Detail + " (" + e.Code + ")")
                    | Ok(files, loaded) ->
                        let id = Ids.text invId.Value
                        Ok
                            { InvestigationId = id
                              Envelope = envelope
                              Files = files
                              Loaded = loaded
                              IsNew = current.IsNone
                              Description = CommandKind.label kind
                              BaseEntries = Session.baseEntries live id
                              BaseCommit = live.Head.Commit
                              BaseTree = live.Head.Tree
                              Moved = false
                              Blobs = Map.empty
                              Commit = None
                              Attempts = 0
                              Generation = 0
                              Received = Map.empty
                              NewHead = None }

    let submit (app: App) : App * EffectRequest list =
        match app.Dialog, App.live app with
        | Some dialog, Some live ->
            match Session.writeAllowed live with
            | Error reason -> { app with Dialog = Some { dialog with Error = Some reason } }, []
            | Ok() ->
                let form = CommandForms.describe None dialog.Kind dialog.Target
                if form.Confirm.IsSome && not dialog.Confirming then
                    { app with Dialog = Some { dialog with Confirming = true; Error = None } }, []
                else
                    match decide live dialog.Kind dialog.Target dialog.Draft app.Route app with
                    | Error message -> { app with Dialog = Some { dialog with Error = Some message; Confirming = false } }, []
                    | Ok w ->
                        let started, effects = { app with Dialog = None; Notice = None } |> startWrite live w
                        if w.IsNew then
                            let routed, nav = navigate (InvestigationRoute(w.InvestigationId, Overview, None)) started
                            routed, effects @ nav
                        else started, effects
        | _ -> app, []

    /// Re-decide a held command against the reloaded remote state (conflict recovery). Never overwrites remote
    /// history: the domain judges the command again at the new version.
    let private reapply (live: Live) (w: Write) (loaded: Loaded) (app: App) =
        let envelope = { w.Envelope with ExpectedVersion = Some loaded.State.Version; RequestedAt = Ids.truncate app.Context.Now }
        let cleared = { live with Reapply = None }
        let result =
            match Decide.execute (Some loaded.State) envelope with
            | Error errors -> Error(refusalText errors)
            | Ok events ->
                match Schemas.set.Force() with
                | Error e -> Error e.Code
                | Ok set -> Store.append set loaded loaded.State.Version events |> Result.mapError (fun e -> e.Detail + " (" + e.Code + ")")
        match result with
        | Error reason ->
            { app with Connection = Connected cleared } |> App.notice Critical ("Your pending change could not be re-applied to the latest version and was not saved: " + reason), []
        | Ok(files, newLoaded) ->
            let rewritten =
                { w with Envelope = envelope; Files = files; Loaded = newLoaded; BaseEntries = Session.baseEntries live w.InvestigationId
                         BaseCommit = live.Head.Commit; BaseTree = live.Head.Tree; Moved = false; Blobs = Map.empty; Commit = None; Attempts = 0
                         Received = Map.empty; NewHead = None }
            startWrite cleared rewritten app

    // ------------------------------------------------------------------------------------------------------------
    // Provider results
    // ------------------------------------------------------------------------------------------------------------

    let private hold (live: Live) (w: Write) (h: Hold) (app: App) =
        { app with Connection = Connected { live with Sync = Held(w, h) } }, []

    let private holdFor (live: Live) (w: Write) (failure: ProviderFailure) app =
        match failure with
        // A commit exists only once the publication got that far; "nothing was sent" would then be untrue.
        | Offline when w.Commit.IsSome -> hold live w (WriteFailed("The change is not on the branch yet. " + GitHub.describe failure)) app
        | Offline -> hold live w (NotSent(GitHub.describe failure)) app
        // A rate limit is a wait, not a refusal of the credential: retryable (review P4).
        | Forbidden true -> hold live w (WriteFailed(GitHub.describe failure)) app
        | Unauthorized | Forbidden _ -> hold live w (WriteDenied(GitHub.describe failure)) app
        | ProviderFailure.OutcomeUnknown -> hold live w (Unknown(GitHub.describe failure)) app
        | _ -> hold live w (WriteFailed(GitHub.describe failure)) app

    let private onConnect (op: Op) (result: Result<string, ProviderFailure>) (app: App) : App * EffectRequest list =
        match app.Connection with
        | Connecting(target, t, _, access, _) ->
            let refuse f = { app with Connection = Refused(target, Session.refusalOf f); Inflight = Map.empty }, []
            match op, result with
            | _, Error f -> refuse f
            | OpRepository, Ok body ->
                match GitHub.parseRepository body with
                | Error f -> refuse f
                | Ok a when not (GitHub.sameRepository target a) ->
                    { app with Connection = Refused(target, Incompatible("GitHub answered for " + a.FullName + ", not " + target.Owner + "/" + target.Repo + " (renamed or transferred). Select the repository by its current name.")); Inflight = Map.empty }, []
                | Ok a -> { app with Connection = Connecting(target, t, ReadingBranch, Some a, None) } |> emit [ OpRef, GitHub.getRef target ]
            | OpRef, Ok body ->
                match GitHub.parseRef body with
                | Error f -> refuse f
                | Ok commit -> { app with Connection = Connecting(target, t, ReadingCommit, access, Some commit) } |> emit [ OpCommit commit, GitHub.getCommit target commit ]
            | OpCommit commit, Ok body ->
                match GitHub.parseCommitTree body with
                | Error f -> refuse f
                | Ok tree ->
                    { app with Connection = Connecting(target, t, ListingTree, access, Some commit) }
                    |> emit [ OpTree { Commit = commit; Tree = tree }, GitHub.getTree target tree ]
            | OpTree head, Ok body ->
                match GitHub.parseTree body, access with
                | Error f, _ -> refuse f
                | Ok entries, Some a ->
                    let catalog, reads, truncated = Session.catalogOf target entries
                    let live =
                        { Target = target; Token = t; Access = a; Head = head; Catalog = catalog; Sync = Idle; Refreshing = false
                          LastVerified = None; Reapply = None; Truncated = truncated }
                    { app with Connection = Connected live } |> emit reads
                | Ok _, None -> refuse (Malformed "connection sequence")
            | _ -> app, []
        | _ -> app, []

    let private onRefresh (op: Op) (result: Result<string, ProviderFailure>) (live: Live) (app: App) =
        let stop f =
            // A change waiting to be re-applied is never dropped: it returns to a visible hold.
            let restored =
                match live.Reapply with
                | Some w -> { live with Refreshing = false; Reapply = None; Sync = Held(w, Conflict("Reload failed, so your change was not re-applied. " + GitHub.describe f)) }
                | None -> { live with Refreshing = false }
            { app with Connection = Connected restored } |> App.notice Critical ("Reload failed: " + GitHub.describe f), []
        match op, result with
        | _, Error f -> stop f
        | OpRef, Ok body ->
            match GitHub.parseRef body with
            | Error f -> stop f
            | Ok commit -> app |> emit [ OpCommit commit, GitHub.getCommit live.Target commit ]
        | OpCommit commit, Ok body ->
            match GitHub.parseCommitTree body with
            | Error f -> stop f
            | Ok tree -> app |> emit [ OpTree { Commit = commit; Tree = tree }, GitHub.getTree live.Target tree ]
        | OpTree head, Ok body ->
            match GitHub.parseTree body with
            | Error f -> stop f
            | Ok entries ->
                let catalog, reads, truncated = Session.catalogOf live.Target entries
                let next = { live with Head = head; Catalog = catalog; Refreshing = false; Truncated = truncated }
                match next.Reapply with
                | Some w when not w.IsNew && not (catalog |> List.exists (fun e -> e.Id = w.InvestigationId)) ->
                    { app with Connection = Connected { next with Reapply = None; Sync = Held(w, Conflict "The investigation is no longer in this workspace; your change was not re-applied.") } } |> emit reads
                | Some w when w.IsNew ->
                    // A new investigation's conflict means its directory now exists: nothing to re-apply onto.
                    { app with Connection = Connected { next with Reapply = None; Sync = Held(w, Conflict "An investigation with this identifier now exists; your new investigation was not written.") } } |> emit reads
                | Some w ->
                    match Session.entry next w.InvestigationId with
                    // Refused at listing time (path, size, duplicate): no blob read will ever end the re-apply (review P2).
                    | Some { Status = (Invalid _ | Unreadable _) } ->
                        { app with Connection = Connected { next with Reapply = None; Sync = Held(w, Conflict "The latest version of this investigation cannot be loaded, so your change was not re-applied.") } } |> emit reads
                    | _ -> { app with Connection = Connected next } |> emit reads
                | None -> { app with Connection = Connected next } |> emit reads
        | _ -> app, []

    /// A blob answer is content-addressed: it is applied only to a catalog entry that still lists exactly that blob
    /// for that file and is still being read (late answers from an earlier listing are ignored, DC-002).
    let private onBlob (id: string) (file: string) (sha: string) (result: Result<string, ProviderFailure>) (live: Live) (app: App) =
        let wants (e: CatalogEntry) =
            e.Id = id && e.Status = Fetching && e.Entries |> List.exists (fun (n, t) -> n = file && t.Sha = sha)
        let update (f: CatalogEntry -> CatalogEntry) =
            { live with Catalog = live.Catalog |> List.map (fun e -> if wants e then f e else e) }
        let next =
            match result |> Result.bind GitHub.parseBlob with
            | Ok bytes -> update (fun e -> Session.receive e file bytes)
            | Error f -> update (fun e -> { e with Status = Unreadable f })
        match next.Reapply, Session.entry next id with
        // Only the listing the reload produced may start a re-apply: never while that reload is still running (review P3).
        | Some w, Some { Status = Valid(_, loaded) } when w.InvestigationId = id && next.Sync = Idle && not next.Refreshing ->
            reapply next w loaded { app with Connection = Connected next }
        | Some w, Some { Status = (Invalid _ | Unreadable _) } when w.InvestigationId = id && not next.Refreshing ->
            { app with Connection = Connected { next with Reapply = None; Sync = Held(w, Conflict "The latest version could not be loaded, so your change was not re-applied.") } }, []
        | _ -> { app with Connection = Connected next }, []

    let private onWrite (op: Op) (result: Result<string, ProviderFailure>) (live: Live) (w: Write) (stage: WriteStage) (app: App) =
        let at s w' = { live with Sync = InFlight(w', s) }
        let continueWith s w' ops = { app with Connection = Connected(at s w') } |> emitGen w'.Generation ops
        // Once the ref update has been sent, no failure is a definite "not written": the branch may have moved
        // (DC-003). Only reconciliation by an independent read can tell.
        let afterRefSent =
            match stage with
            | UpdatingRef | Reconciling | VerifyingRef | VerifyingCommit | VerifyingTree | VerifyingBlobs -> true
            | CreatingBlobs | CreatingTree | CreatingCommit | ReloadingRef | ReloadingCommit | ReloadingTree -> false
        let unknown detail = hold live w (Unknown detail) app
        let fail f =
            if afterRefSent then unknown ("The branch update may have been applied (" + GitHub.describe f + ") Confirm what GitHub received before doing anything else.")
            else holdFor live w f app
        match op, result with
        | OpWriteBlob file, Ok body ->
            match GitHub.parseCreatedSha body with
            | Error f -> fail f
            | Ok sha ->
                let w' = { w with Blobs = w.Blobs |> Map.add file sha }
                if w'.Blobs.Count < Session.fileNames.Length then { app with Connection = Connected(at stage w') }, []
                else continueWith CreatingTree w' [ Session.treeRequest live w' ]
        | OpWriteTree, Ok body ->
            match GitHub.parseCreatedSha body with
            | Error f -> fail f
            | Ok tree -> continueWith CreatingCommit w [ OpWriteCommit, GitHub.createCommit live.Target (Session.commitMessage w) tree w.BaseCommit ]
        | OpWriteCommit, Ok body ->
            match GitHub.parseCreatedSha body with
            | Error f -> fail f
            | Ok commit -> continueWith UpdatingRef { w with Commit = Some commit } [ OpWriteRef, GitHub.updateRef live.Target commit ]
        | OpWriteRef, Ok _ -> continueWith VerifyingRef w [ OpVerifyRef, GitHub.getRef live.Target ]
        | OpWriteRef, Error NotFastForward ->
            if w.Attempts >= Session.maxRebases then hold live w (Conflict "The branch kept moving while saving.") app
            else continueWith ReloadingRef w [ OpReloadRef, GitHub.getRef live.Target ]
        | OpWriteRef, Error ProviderFailure.OutcomeUnknown -> continueWith Reconciling w [ OpReconcileRef, GitHub.getRef live.Target ]
        | OpReconcileRef, Ok body ->
            match GitHub.parseRef body with
            | Error f -> fail f
            | Ok sha when Some sha = w.Commit -> continueWith VerifyingCommit w [ OpVerifyCommit sha, GitHub.getCommit live.Target sha ]
            | Ok sha when sha = w.BaseCommit ->
                match w.Commit with
                | Some c -> continueWith UpdatingRef w [ OpWriteRef, GitHub.updateRef live.Target c ]
                // No commit was created: nothing could have been applied; publish again from the start.
                | None -> continueWith CreatingBlobs { w with Blobs = Map.empty } (Session.blobRequests live w)
            | Ok sha ->
                // The branch moved: our commit may be in its history (applied, then built upon) or not (not applied).
                match w.Commit with
                | Some ours -> continueWith Reconciling w [ OpReconcileCompare sha, GitHub.compare live.Target ours sha ]
                | None -> continueWith ReloadingCommit w [ OpReloadCommit sha, GitHub.getCommit live.Target sha ]
        | OpReconcileCompare sha, Ok body ->
            match GitHub.parseCompareStatus body, w.Commit with
            | Ok("identical" | "ahead"), Some ours -> continueWith VerifyingCommit { w with Moved = true } [ OpVerifyCommit ours, GitHub.getCommit live.Target ours ]
            | Ok _, _ -> continueWith ReloadingCommit w [ OpReloadCommit sha, GitHub.getCommit live.Target sha ]
            | Error f, _ -> fail f
        | OpReloadRef, Ok body ->
            match GitHub.parseRef body with
            | Error f -> fail f
            | Ok sha -> continueWith ReloadingCommit w [ OpReloadCommit sha, GitHub.getCommit live.Target sha ]
        | OpReloadCommit sha, Ok body ->
            match GitHub.parseCommitTree body with
            | Error f -> fail f
            | Ok tree -> continueWith ReloadingTree w [ OpReloadTree { Commit = sha; Tree = tree }, GitHub.getTree live.Target tree ]
        | OpReloadTree head, Ok body ->
            match GitHub.parseTree body with
            | Error f -> fail f
            | Ok entries ->
                let now =
                    GitHub.investigations (Target.prefix live.Target) entries
                    |> List.tryFind (fun (id, _) -> id = w.InvestigationId)
                    |> Option.map (fun (_, files) -> files |> List.map (fun (n, e) -> n, e.Sha) |> Map.ofList)
                    |> Option.defaultValue Map.empty
                if now = w.BaseEntries then
                    // The branch moved but this investigation did not: rebase onto the new head (CONTRACT §9.4.2a).
                    // The catalog's read point (live.Head) does not move: other investigations were not re-read.
                    let w' = { w with Attempts = w.Attempts + 1; BaseCommit = head.Commit; BaseTree = head.Tree; Moved = true }
                    if Session.fileNames |> List.forall w'.Blobs.ContainsKey then
                        { app with Connection = Connected { live with Sync = InFlight(w', CreatingTree) } } |> emitGen w'.Generation [ Session.treeRequest live w' ]
                    else
                        // Blob creation never completed (an unknown or cancelled answer): create them again (review P1).
                        let again = { w' with Blobs = Map.empty }
                        { app with Connection = Connected { live with Sync = InFlight(again, CreatingBlobs) } } |> emitGen again.Generation (Session.blobRequests live again)
                else
                    hold live w (Conflict "Someone else changed this investigation on GitHub after you loaded it. Your change was not written.") app
        | OpVerifyRef, Ok body ->
            match GitHub.parseRef body, w.Commit with
            | Ok sha, Some commit when sha = commit -> continueWith VerifyingCommit w [ OpVerifyCommit commit, GitHub.getCommit live.Target commit ]
            | Ok _, Some commit -> continueWith VerifyingCommit w [ OpVerifyCommit commit, GitHub.getCommit live.Target commit ]
            | Error f, _ -> fail f
            | _, None -> fail (Malformed "write sequence")
        | OpVerifyCommit commit, Ok body ->
            match GitHub.parseCommitTree body with
            | Error f -> fail f
            | Ok tree -> continueWith VerifyingTree w [ OpVerifyTree { Commit = commit; Tree = tree }, GitHub.getTree live.Target tree ]
        | OpVerifyTree head, Ok body ->
            match GitHub.parseTree body with
            | Error f -> fail f
            | Ok entries ->
                let found =
                    GitHub.investigations (Target.prefix live.Target) entries
                    |> List.tryFind (fun (id, _) -> id = w.InvestigationId)
                    |> Option.map snd
                    |> Option.defaultValue []
                let expected = Session.fileNames |> List.map (fun f -> f, GitHub.blobSha (Session.filesBytes w.Files f)) |> Map.ofList
                let actual = found |> List.map (fun (n, e) -> n, e.Sha) |> Map.ofList
                if actual <> expected then unknown "The branch was updated, but the commit read back does not contain exactly the files that were written."
                else
                    let w' = { w with NewHead = Some head; Received = Map.empty }
                    continueWith VerifyingBlobs w' (found |> List.map (fun (n, e) -> OpVerifyBlob n, GitHub.getBlob live.Target e.Sha))
        | OpVerifyBlob file, Ok body ->
            match GitHub.parseBlob body with
            | Error f -> fail f
            | Ok bytes ->
                let w' = { w with Received = w.Received |> Map.add file bytes }
                if w'.Received.Count < Session.fileNames.Length then { app with Connection = Connected(at stage w') }, []
                else
                    // Independent read-back: the bytes GitHub serves at the new commit must equal what was written and
                    // pass the full reader pipeline again (CONTRACT §9.4.3).
                    let identical = Session.fileNames |> List.forall (fun f -> w'.Received[f] = Session.filesBytes w.Files f)
                    let revalidated =
                        match Schemas.set.Force() with
                        | Ok set -> Store.ofFileMap w.InvestigationId w'.Received |> Result.bind (Store.validate set)
                        | Error e -> Error e
                    match identical, revalidated, w'.NewHead with
                    | true, Ok reloaded, Some head ->
                        let entries =
                            Session.paths live.Target w.InvestigationId
                            |> List.map (fun (f, p) -> f, { Path = p; Kind = "blob"; Sha = GitHub.blobSha (Session.filesBytes w.Files f); Size = Some(int64 (Session.filesBytes w.Files f).Length) })
                        let newEntry = { Id = w.InvestigationId; Entries = entries; Received = w'.Received; Status = Valid(w.Files, reloaded) }
                        let catalog =
                            if live.Catalog |> List.exists (fun e -> e.Id = w.InvestigationId) then
                                live.Catalog |> List.map (fun e -> if e.Id = w.InvestigationId then newEntry else e)
                            else live.Catalog @ [ newEntry ]
                        let consistent = not w.Moved && w.BaseCommit = live.Head.Commit
                        let saved =
                            { app with
                                Connection =
                                    Connected
                                        { live with
                                            Head = (if consistent then head else live.Head)
                                            Catalog = catalog
                                            Sync = Idle
                                            LastVerified = Some(head.Commit, app.Context.Now) } }
                            |> App.notice Positive ("Saved to GitHub and verified by an independent read-back (commit " + head.Commit.Substring(0, 7) + ").")
                        // Other investigations may have changed on the branch meanwhile: re-read before the next decision.
                        if consistent then saved, [] else refresh saved
                    | _ -> unknown "The branch was updated, but the bytes read back from GitHub do not match what was written."
        | _, Error f -> fail f
        | _ -> app, []

    let private onEffect (result: EffectResult) (app: App) : App * EffectRequest list =
        match result with
        | EffectResult.HttpResult(CorrelationId id, o) ->
            match app.Inflight |> Map.tryFind id with
            | None -> app, [] // stale or cancelled: answers a question no longer asked
            | Some(op, generation) ->
                let app = { app with Inflight = app.Inflight |> Map.remove id }
                let r = outcome o
                match app.Connection, op with
                | Connecting _, _ -> onConnect op r app
                | Connected live, OpBlob(inv, file, sha) -> onBlob inv file sha r live app
                | Connected live, (OpRef | OpCommit _ | OpTree _) when generation = 0 && live.Refreshing -> onRefresh op r live app
                | Connected live, _ ->
                    match live.Sync with
                    | InFlight(w, stage) when generation <> 0 && w.Generation = generation -> onWrite op r live w stage app
                    | _ -> app, [] // an answer for another write generation
                | _ -> app, []
        | EffectResult.NavigationResult(_, NavigationOutcome.Success location) -> { app with Route = Route.parse location.Hash }, []
        | _ -> app, []

    // ------------------------------------------------------------------------------------------------------------
    // Semantic events
    // ------------------------------------------------------------------------------------------------------------

    let private selectedEntity (app: App) =
        match app.Route with
        | InvestigationRoute(_, _, Some e) -> (match Guid.TryParse e with true, g -> Some g | _ -> None)
        | _ -> None

    let private updateDraft (f: FormDraft -> FormDraft) (app: App) =
        match app.Dialog with
        | Some d -> { app with Dialog = Some { d with Draft = f d.Draft; Error = None; Confirming = false } }
        | None -> app

    let private slotOf (name: string) =
        match name with
        | "cmd.textA" -> Some TextA
        | "cmd.textB" -> Some TextB
        | "cmd.enumA" -> Some EnumA
        | "cmd.enumB" -> Some EnumB
        | "cmd.refA" -> Some RefA
        | _ -> None

    let private onEvent (e: SemanticEvent) (app: App) : App * EffectRequest list * CorrelationId list =
        let value = e.Value |> Option.defaultValue ""
        let plain (a: App) = a, [], []
        let withEffects (a: App, fx) = a, fx, []
        match e.Name with
        | "connect.owner" | "connect.repo" | "connect.branch" | "connect.root" | "connect.token" ->
            plain { app with ConnectForm = app.ConnectForm |> Map.add (e.Name.Substring 8) value }
        | "connect.submit" -> withEffects (connect app)
        | "connect.cancel" | "disconnect" ->
            let a, cancelled = disconnect "Disconnected. The credential was discarded from memory and remote data was removed from the screen." app
            a, [], cancelled
        | "refresh" -> withEffects (refresh app)
        | "cmd.open" ->
            match e.Key |> Option.bind CommandKind.tryParse with
            | Some kind ->
                let target = if List.contains kind Capabilities.investigationCommands || kind = NewInvestigation then None else selectedEntity app
                plain { app with Dialog = Some { Kind = kind; Target = target; Draft = FormDraft.empty; Error = None; Confirming = false } }
            | None -> plain app
        | "cmd.new" -> plain { app with Dialog = Some { Kind = NewInvestigation; Target = None; Draft = FormDraft.empty; Error = None; Confirming = false } }
        | "cmd.refsB" ->
            match e.Key with
            | Some key ->
                plain (updateDraft (fun d -> { d with Selected = (if e.Checked = Some true || (e.Checked.IsNone && not (d.Selected.Contains key)) then d.Selected.Add key else d.Selected.Remove key) }) app)
            | None -> plain app
        | "cmd.submit" -> withEffects (submit app)
        | "cmd.cancel" -> plain { app with Dialog = None }
        | "sync.retry" ->
            match App.live app with
            | Some live ->
                match live.Sync with
                | Held(w, (NotSent _ | WriteFailed _)) when not live.Refreshing ->
                    withEffects (startWrite live { w with Blobs = Map.empty; Commit = None; Received = Map.empty; NewHead = None } app)
                | _ -> plain app
            | None -> plain app
        | "sync.reconcile" ->
            match App.live app with
            | Some({ Sync = Held(w, Unknown _) } as live) when not live.Refreshing ->
                // A fresh generation: late answers to the held attempt cannot abort the reconciliation (review P5).
                let w' = { w with Generation = app.Counter + 1 }
                withEffects ({ app with Connection = Connected { live with Sync = InFlight(w', Reconciling) } } |> emitGen w'.Generation [ OpReconcileRef, GitHub.getRef live.Target ])
            | _ -> plain app
        | "sync.discard" ->
            match App.live app with
            // Only a held change can be discarded; a write in flight cannot be called "not written" (DC-002).
            | Some({ Sync = Held(_, hold) } as live) ->
                let text =
                    match hold with
                    | Unknown _ -> "Your pending change was discarded from this tab. It may already be on GitHub; the workspace is being reloaded."
                    | _ -> "Your pending change was discarded; nothing was written."
                withEffects (refresh ({ app with Connection = Connected { live with Sync = Idle; Reapply = None } } |> App.notice Neutral text))
            | _ -> plain app
        | "sync.reapply" ->
            match App.live app with
            | Some({ Sync = Held(w, Conflict _) } as live) ->
                withEffects (refresh { app with Connection = Connected { live with Sync = Idle; Reapply = Some w } })
            | _ -> plain app
        | "auth.renew" ->
            // Replace an expired or narrower credential without losing the held change, then publish it again.
            let credential = app.ConnectForm |> Map.tryFind "token" |> Option.defaultValue ""
            let form = { app with ConnectForm = app.ConnectForm |> Map.remove "token" }
            match App.live form with
            | Some({ Sync = Held(w, WriteDenied _) } as live) when not (String.IsNullOrWhiteSpace credential) ->
                let renewed = { live with Token = credential.Trim() }
                withEffects (startWrite renewed { w with Blobs = Map.empty; Commit = None; Received = Map.empty; NewHead = None } { form with Connection = Connected renewed })
            | Some({ Sync = Held(w, Unknown _) } as live) when not (String.IsNullOrWhiteSpace credential) && not live.Refreshing ->
                // The outcome is unknown: with the new credential, first confirm what GitHub received.
                let w' = { w with Generation = form.Counter + 1 }
                let renewed = { live with Token = credential.Trim(); Sync = InFlight(w', Reconciling) }
                withEffects ({ form with Connection = Connected renewed } |> emitGen w'.Generation [ OpReconcileRef, GitHub.getRef live.Target ])
            | _ -> plain (form |> App.notice Critical "Enter a new token to retry the held change.")
        | "timeline.filter" -> plain { app with TimelineFilter = value }
        | "dashboard.sort" -> plain { app with SortBy = value }
        | "notice.dismiss" -> plain { app with Notice = None }
        | name ->
            match slotOf name with
            | Some slot -> plain (updateDraft (fun d -> { d with Values = d.Values |> Map.add slot value }) app)
            | None -> plain app

    // ------------------------------------------------------------------------------------------------------------
    // The boundary
    // ------------------------------------------------------------------------------------------------------------

    let private respond (app: App) effects cancellations handshake : App * EngineToBrowserMessage =
        app, { View = View.project app; Effects = effects; Cancellations = cancellations; Handshake = handshake }

    let handle (app: App) (message: BrowserToEngineMessage) : App * EngineToBrowserMessage =
        match app.Boot, message with
        | _, BrowserToEngineMessage.Initialize(_, location, offer) ->
            match Handshake.answer offer requirements with
            | EngineHandshake.Accepted _ as accepted ->
                respond { app with Boot = Running; Base = location.Path; Route = Route.parse location.Hash } [] [] (Some accepted)
            | EngineHandshake.Rejected reason as rejected -> respond { app with Boot = HandshakeRefused(sprintf "%A" reason) } [] [] (Some rejected)
        | HandshakeRefused _, _ -> respond app [] [] None
        | _, BrowserToEngineMessage.LocationChanged location -> respond { app with Route = Route.parse location.Hash; Dialog = None } [] [] None
        | _, BrowserToEngineMessage.Event e ->
            let next, effects, cancellations = onEvent e app
            respond next effects cancellations None
        | _, BrowserToEngineMessage.EffectResult r ->
            let next, effects = onEffect r app
            respond next effects [] None
        | _, BrowserToEngineMessage.CapabilityFact _ -> respond app [] [] None

    /// Supply the boundary's time and entropy for the next message.
    let withContext (seed: Guid) (now: DateTimeOffset) (app: App) = { app with Context = { Now = now; Seed = seed } }

    /// One serialized Limen message in, one serialized response out. Messages outside the contract do not change
    /// state (they are a compatibility failure, not an application event).
    let dispatch (app: App) (json: string) : App * string =
        match Codec.parseBrowserToEngineMessage json with
        | Error error ->
            let _, response = respond (app |> App.notice Critical ("A browser message did not match the Limen contract at " + error.Path + ".")) [] [] None
            app, Codec.serializeEngineToBrowserMessage response
        | Ok message ->
            let next, response = handle app message
            next, Codec.serializeEngineToBrowserMessage response
