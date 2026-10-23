namespace Causa.Engine

open System
open IndyInitRootCauseInvestigator.Engine
open Causa.Contract

/// One investigation directory found under the selected data root, and what reading it produced.
type EntryStatus =
    | Fetching
    | Valid of files: InvestigationFiles * loaded: Loaded
    | Invalid of CausaError
    | Unreadable of ProviderFailure

type CatalogEntry =
    { Id: string
      Entries: (string * TreeEntry) list
      Received: Map<string, byte[]>
      Status: EntryStatus }

/// A decided command whose canonical file set is being published (CONTRACT §9.2–§9.4).
type Write =
    { InvestigationId: string
      Envelope: CommandEnvelope
      Files: InvestigationFiles
      Loaded: Loaded
      IsNew: bool
      Description: string
      /// Investigation file name → blob SHA at the commit the decision was based on (empty for a new investigation).
      BaseEntries: Map<string, string>
      BaseCommit: string
      /// The tree of BaseCommit. Every publication is built on, and parented by, the exact commit whose state the
      /// decision was made against, so any intervening change forces the non-fast-forward path (defect class DC-001).
      BaseTree: string
      /// True once the write was rebased or found applied under a moved branch: the catalog must be re-read.
      Moved: bool
      Blobs: Map<string, string>
      Commit: string option
      Attempts: int
      Received: Map<string, byte[]>
      /// The head the write produced, once its commit and tree are known (set during verification).
      NewHead: Head option }

type WriteStage =
    | CreatingBlobs
    | CreatingTree
    | CreatingCommit
    | UpdatingRef
    | ReloadingRef
    | ReloadingCommit
    | ReloadingTree
    | Reconciling
    | VerifyingRef
    | VerifyingCommit
    | VerifyingTree
    | VerifyingBlobs

/// Why a decided change is held locally instead of being published.
type Hold =
    | NotSent of reason: string
    | Conflict of detail: string
    | WriteDenied of detail: string
    | WriteFailed of detail: string
    | Unknown of detail: string

type Sync =
    | Idle
    | InFlight of Write * WriteStage
    | Held of Write * Hold

type ConnectStage =
    | CheckingRepository
    | ReadingBranch
    | ReadingCommit
    | ListingTree

type Refusal =
    | AccessDenied of string
    | NotFoundRefusal of string
    | OfflineRefusal of string
    | RateLimited of string
    | Incompatible of string
    | ProviderError of string

type Live =
    { Target: Target
      /// The credential: engine memory only. Never projected, persisted, logged or written to a file (EXT-DATA-004).
      Token: string
      Access: Access
      /// The head at which the catalog was read. It moves only when the catalog is re-read consistently, or when
      /// a write that was parented by exactly this head is verified (then only that investigation changed).
      Head: Head
      Catalog: CatalogEntry list
      Sync: Sync
      Refreshing: bool
      LastVerified: (string * DateTimeOffset) option
      /// A held command to re-decide once the investigation has been reloaded (conflict recovery).
      Reapply: Write option }

type Connection =
    | Disconnected
    | Connecting of Target * token: string * ConnectStage * Access option * string option
    | Connected of Live
    | Refused of Target * Refusal

/// The correlation of an in-flight provider request with the question it answers.
type Op =
    | OpRepository
    | OpRef
    | OpCommit of commit: string
    | OpTree of head: Head
    | OpBlob of investigation: string * file: string
    | OpWriteBlob of file: string
    | OpWriteTree
    | OpWriteCommit
    | OpWriteRef
    | OpReloadRef
    | OpReloadCommit of commit: string
    | OpReloadTree of head: Head
    | OpReconcileRef
    | OpReconcileCompare of ref: string
    | OpVerifyRef
    | OpVerifyCommit of commit: string
    | OpVerifyTree of head: Head
    | OpVerifyBlob of file: string

[<RequireQualifiedAccess>]
module Session =
    let actor = Actor.human "causa-investigator"
    let fileNames = Store.fileNames
    let maxInvestigations = 40
    let maxRebases = 3

    let schemaSet () = Schemas.set.Force()

    let entry (live: Live) (id: string) = live.Catalog |> List.tryFind (fun e -> e.Id = id)

    let loaded (live: Live) (id: string) =
        match entry live id with
        | Some { Status = Valid(files, l) } -> Some(files, l)
        | _ -> None

    /// What the interface shows for an investigation: the locally decided state while a change is pending, otherwise
    /// the verified remote state. `true` marks a local, not yet remotely acknowledged view.
    let displayed (live: Live) (id: string) : (Loaded * bool) option =
        let pending =
            match live.Sync with
            | InFlight(w, _) | Held(w, _) when w.InvestigationId = id -> Some(w.Loaded, true)
            | _ -> None
        match pending with
        | Some p -> Some p
        | None -> loaded live id |> Option.map (fun (_, l) -> l, false)

    let writeAllowed (live: Live) : Result<unit, string> =
        if not (Target.writable live.Target) then Error "The frozen v1.0.0 fixtures are read-only. Connect to a workspaces/demo/<run> namespace to write."
        elif not live.Access.CanPush then Error "This credential's user has read-only access to the repository."
        else
            match live.Sync with
            | InFlight _ -> Error "A change is being saved to GitHub."
            | Held _ -> Error "Resolve the pending change first (retry or discard)."
            | Idle -> if live.Refreshing then Error "The workspace is being reloaded." else Ok()

    let refusalOf (failure: ProviderFailure) : Refusal =
        match failure with
        | Unauthorized | Forbidden false -> AccessDenied(GitHub.describe failure)
        | Forbidden true -> RateLimited(GitHub.describe failure)
        | NotFound -> NotFoundRefusal(GitHub.describe failure)
        | Offline -> OfflineRefusal(GitHub.describe failure)
        | Malformed _ -> Incompatible(GitHub.describe failure)
        | NotFastForward | ProviderFailure.Rejected _ | ProviderFailure.OutcomeUnknown -> ProviderError(GitHub.describe failure)

    /// Turn a completed listing into catalog entries and the blob reads they need.
    let catalogOf (target: Target) (tree: TreeEntry list) : CatalogEntry list * (Op * ApiRequest) list =
        let groups = GitHub.investigations (Target.prefix target) tree |> List.truncate maxInvestigations
        let entries = groups |> List.map (fun (id, files) -> { Id = id; Entries = files; Received = Map.empty; Status = Fetching })
        let reads =
            groups
            |> List.collect (fun (id, files) ->
                let wellFormed = files |> List.map fst |> List.sort = List.sort fileNames && files |> List.forall (fun (_, e) -> e.Kind = "blob")
                if wellFormed then files |> List.map (fun (name, e) -> OpBlob(id, name), GitHub.getBlob target e.Sha) else [])
        let settled =
            entries
            |> List.map (fun e ->
                let wellFormed = e.Entries |> List.map fst |> List.sort = List.sort fileNames && e.Entries |> List.forall (fun (_, x) -> x.Kind = "blob")
                if wellFormed then e
                else
                    // Every directory entry counts (CONTRACT §3.3); the reader reports the exact path problem.
                    let names = e.Entries |> List.map (fun (n, _) -> n, [||]) |> Map.ofList
                    match Store.ofFileMap e.Id names with
                    | Error err -> { e with Status = Invalid err }
                    | Ok _ -> { e with Status = Invalid(CausaError.create Codes.pathInvalid "unreadable investigation directory") })
        settled, reads

    /// A received blob; when the third file arrives the directory is validated through the full contract pipeline,
    /// including the domain's History.validate (EXT-DATA-005). Invalid data fails closed and is never partially shown.
    let receive (entry: CatalogEntry) (file: string) (bytes: byte[]) : CatalogEntry =
        let received = entry.Received |> Map.add file bytes
        if received.Count < fileNames.Length then { entry with Received = received }
        else
            let status =
                match schemaSet () with
                | Error e -> Invalid e
                | Ok set ->
                    match Store.ofFileMap entry.Id received |> Result.bind (fun files -> Store.validate set files |> Result.map (fun l -> files, l)) with
                    | Ok(files, l) -> Valid(files, l)
                    | Error e -> Invalid e
            { entry with Received = received; Status = status }

    let baseEntries (live: Live) (id: string) : Map<string, string> =
        match entry live id with
        | Some e -> e.Entries |> List.map (fun (n, t) -> n, t.Sha) |> Map.ofList
        | None -> Map.empty

    let paths (target: Target) (id: string) = fileNames |> List.map (fun f -> f, Target.prefix target + id + "/" + f)

    let filesBytes (files: InvestigationFiles) (name: string) =
        match name with
        | "manifest.json" -> files.Manifest
        | "events.ndjson" -> files.Events
        | _ -> files.Snapshot

    let blobRequests (live: Live) (w: Write) =
        fileNames |> List.map (fun f -> OpWriteBlob f, GitHub.createBlob live.Target (filesBytes w.Files f))

    let treeRequest (live: Live) (w: Write) =
        let entries = paths live.Target w.InvestigationId |> List.map (fun (f, p) -> p, w.Blobs[f])
        OpWriteTree, GitHub.createTree live.Target w.BaseTree entries

    let commitMessage (w: Write) =
        "causa: " + w.Description + " (" + w.InvestigationId + " v" + string w.Loaded.State.Version + ")\n\nCommand " + Ids.text w.Envelope.CommandId
