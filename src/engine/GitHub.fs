namespace Causa.Engine

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

/// Where investigation records live in the external data repository. The frozen v1.0.0 fixtures are readable but
/// never writable (CONTRACT §3.4); writes go only to an isolated `workspaces/demo/<runId>/` namespace (§9.4.4).
type DataRoot =
    | FrozenFixtures
    | Workspace of runId: string

/// The explicitly selected data location (EXT-DATA-003). It carries no credential.
type Target =
    { Owner: string
      Repo: string
      Branch: string
      Root: DataRoot }

/// Repository facts read on connect. `CanPush` is the repository role GitHub reports for the credential's *user*.
/// It is advisory and never gates writing (DC-004): a fine-grained token may be narrower than the role, and a GitHub
/// App installation token (the Actions GITHUB_TOKEN) is reported with `push: false` although it can write. GitHub's
/// answer to the write itself decides; a refusal is held as WriteDenied with the change preserved.
type Access =
    { CanPush: bool option
      IsPrivate: bool
      FullName: string }

type Head = { Commit: string; Tree: string }

type TreeEntry = { Path: string; Kind: string; Sha: string; Size: int64 option }

/// One GitHub REST conversation step, described as data. The Limen kernel performs it (CONTRACT §9.4).
type ApiRequest =
    { Method: string
      Path: string
      Body: string option
      /// A read of mutable state (a branch ref, the repository). GitHub answers with `Cache-Control: max-age=60`, so
      /// such a read must never be answered from a cache: it is sent under a unique URL (DC-005).
      Fresh: bool }

/// Why a provider step did not succeed. Unknown means the request may have reached GitHub (never success).
type ProviderFailure =
    | Unauthorized
    | Forbidden of rateLimited: bool
    | NotFound
    | NotFastForward
    | Rejected of status: int64 * detail: string
    | Offline
    | OutcomeUnknown
    | Malformed of detail: string

[<RequireQualifiedAccess>]
module Target =
    // \z, not $: CONTRACT §2.13 (ECMA-262 '$' never matches before a final LF; .NET's does).
    let private ownerPattern = Regex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})\z", RegexOptions.CultureInvariant)
    let private repoPattern = Regex(@"^[A-Za-z0-9._-]{1,100}\z", RegexOptions.CultureInvariant)
    let private branchPattern = Regex(@"^[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*\z", RegexOptions.CultureInvariant)
    let private runIdPattern = Regex(@"^[a-z0-9][a-z0-9-]{0,63}\z", RegexOptions.CultureInvariant)

    let ownerValid (s: string) = ownerPattern.IsMatch s
    let repoValid (s: string) = repoPattern.IsMatch s && s <> "." && s <> ".."
    let branchValid (s: string) =
        branchPattern.IsMatch s
        && not (s.Contains("..", StringComparison.Ordinal))
        && s.Split('/') |> Array.forall (fun seg -> not (seg.StartsWith(".", StringComparison.Ordinal)) && not (seg.EndsWith(".lock", StringComparison.Ordinal)))
    let runIdValid (s: string) = runIdPattern.IsMatch s

    /// Validate an explicit selection. Every field is required; nothing is inferred from the credential.
    let create (owner: string) (repo: string) (branch: string) (root: string) : Result<Target, string list> =
        let rootValue =
            match root.Trim() with
            | "fixtures" | "fixtures/v1.0.0" -> Ok FrozenFixtures
            | r when runIdValid r -> Ok(Workspace r)
            | _ -> Error "Workspace must be 'fixtures' (read-only frozen set) or a run id: lowercase letters, digits and '-', up to 64 characters."
        let problems =
            [ if not (ownerValid (owner.Trim())) then "Owner is not a valid GitHub user or organization name."
              if not (repoValid (repo.Trim())) then "Repository is not a valid GitHub repository name."
              if not (branchValid (branch.Trim())) then "Branch is not a valid branch name."
              match rootValue with
              | Error e -> e
              | Ok _ -> () ]
        match problems, rootValue with
        | [], Ok r -> Ok { Owner = owner.Trim(); Repo = repo.Trim(); Branch = branch.Trim(); Root = r }
        | ps, _ -> Error ps

    let writable (t: Target) = match t.Root with Workspace _ -> true | FrozenFixtures -> false

    /// The investigations prefix, with a trailing slash.
    let prefix (t: Target) =
        match t.Root with
        | FrozenFixtures -> "fixtures/v1.0.0/investigations/"
        | Workspace runId -> "workspaces/demo/" + runId + "/investigations/"

    let rootLabel (t: Target) =
        match t.Root with
        | FrozenFixtures -> "fixtures/v1.0.0 (frozen, read-only)"
        | Workspace runId -> "workspaces/demo/" + runId

    let identity (t: Target) = t.Owner + "/" + t.Repo + "@" + t.Branch + ":" + rootLabel t

[<RequireQualifiedAccess>]
module GitHub =
    let apiBase = "https://api.github.com"
    let apiVersion = "2022-11-28"

    let private repo (t: Target) = "/repos/" + Uri.EscapeDataString t.Owner + "/" + Uri.EscapeDataString t.Repo
    let private refPath (t: Target) = t.Branch.Split('/') |> Array.map Uri.EscapeDataString |> String.concat "/"
    let private get path = { Method = "GET"; Path = path; Body = None; Fresh = false }
    let private getFresh path = { get path with Fresh = true }

    let private json (write: Utf8JsonWriter -> unit) =
        use stream = new IO.MemoryStream()
        (use w = new Utf8JsonWriter(stream)
         write w)
        Encoding.UTF8.GetString(stream.ToArray())

    let getRepository t = getFresh (repo t)
    let getRef t = getFresh (repo t + "/git/ref/heads/" + refPath t)
    let getCommit t (sha: string) = get (repo t + "/git/commits/" + sha)
    let getTree t (sha: string) = get (repo t + "/git/trees/" + sha + "?recursive=1")
    let getBlob t (sha: string) = get (repo t + "/git/blobs/" + sha)
    let compare t (baseSha: string) (headSha: string) = get (repo t + "/compare/" + baseSha + "..." + headSha)

    /// The one publication request: GitHub's GraphQL createCommitOnBranch creates the commit and moves the branch in
    /// one atomic step, and refuses unless the branch still names `expectedHead` (the commit the decision was made
    /// against). REST cannot express that guard: its non-forced ref update still fast-forwards from an older, reset
    /// head (DC-005, closure review 6).
    let commitOnBranch (t: Target) (expectedHead: string) (headline: string) (body: string) (files: (string * byte[]) list) =
        let mutation =
            "mutation($input: CreateCommitOnBranchInput!) { createCommitOnBranch(input: $input) { commit { oid } } }"
        { Method = "POST"
          Path = "/graphql"
          Body =
            Some(
                json (fun w ->
                    w.WriteStartObject()
                    w.WriteString("query", mutation)
                    w.WriteStartObject "variables"
                    w.WriteStartObject "input"
                    w.WriteStartObject "branch"
                    w.WriteString("repositoryNameWithOwner", t.Owner + "/" + t.Repo)
                    w.WriteString("branchName", t.Branch)
                    w.WriteEndObject()
                    w.WriteString("expectedHeadOid", expectedHead)
                    w.WriteStartObject "message"
                    w.WriteString("headline", headline)
                    w.WriteString("body", body)
                    w.WriteEndObject()
                    w.WriteStartObject "fileChanges"
                    w.WriteStartArray "additions"
                    files
                    |> List.iter (fun (path, bytes) ->
                        w.WriteStartObject()
                        w.WriteString("path", path)
                        w.WriteString("contents", Convert.ToBase64String bytes)
                        w.WriteEndObject())
                    w.WriteEndArray()
                    w.WriteEndObject()
                    w.WriteEndObject()
                    w.WriteEndObject()
                    w.WriteEndObject())
            )
          Fresh = false }

    // ------------------------------------------------------------------------------------------------------------
    // Interpreting responses
    // ------------------------------------------------------------------------------------------------------------

    let private shaPattern = Regex(@"^[0-9a-f]{40}\z", RegexOptions.CultureInvariant)

    let private parse (body: string) (read: JsonElement -> 'T option) (what: string) : Result<'T, ProviderFailure> =
        try
            use doc = JsonDocument.Parse body
            match read doc.RootElement with
            | Some v -> Ok v
            | None -> Error(Malformed what)
        with _ -> Error(Malformed what)

    let private prop (name: string) (e: JsonElement) =
        match e.ValueKind with
        | JsonValueKind.Object ->
            match e.TryGetProperty name with
            | true, v -> Some v
            | _ -> None
        | _ -> None

    let private str name e =
        prop name e |> Option.bind (fun v -> if v.ValueKind = JsonValueKind.String then Option.ofObj (v.GetString()) else None)

    let private flag name e =
        prop name e |> Option.bind (fun v -> match v.ValueKind with JsonValueKind.True -> Some true | JsonValueKind.False -> Some false | _ -> None)

    let private sha name e = str name e |> Option.filter shaPattern.IsMatch

    /// Map an HTTP status (with its body) to success or a typed failure.
    let classify (status: int64) (body: string) : Result<string, ProviderFailure> =
        match status with
        | s when s >= 200L && s < 300L -> Ok body
        | 401L -> Error Unauthorized
        | 403L | 429L ->
            let lower = body.ToLowerInvariant()
            Error(Forbidden(status = 429L || lower.Contains "rate limit"))
        | 404L -> Error NotFound
        | 409L -> Error(Rejected(status, "conflict"))
        | 422L when body.Contains("fast forward", StringComparison.OrdinalIgnoreCase) || body.Contains("fast-forward", StringComparison.OrdinalIgnoreCase) -> Error NotFastForward
        | s -> Error(Rejected(s, "HTTP " + string s))

    /// GitHub follows renames and transfers by redirect; the repository answering must be the one selected.
    let sameRepository (t: Target) (a: Access) =
        String.Equals(a.FullName, t.Owner + "/" + t.Repo, StringComparison.OrdinalIgnoreCase)

    let parseRepository body =
        parse body (fun e ->
            match str "full_name" e, flag "private" e with
            | Some name, Some isPrivate ->
                let push = prop "permissions" e |> Option.bind (flag "push")
                Some { CanPush = push; IsPrivate = isPrivate; FullName = name }
            | _ -> None) "repository"

    let parseRef body =
        parse body (fun e -> prop "object" e |> Option.bind (sha "sha")) "ref"

    let parseCommitTree body =
        parse body (fun e -> prop "tree" e |> Option.bind (sha "sha")) "commit"

    /// The answer to createCommitOnBranch. GraphQL reports refusals as `errors` in a 200 answer: a branch that no
    /// longer names the expected head is the non-fast-forward case; permission and missing-object errors keep their
    /// meaning; anything else is a rejection.
    let parseCommitOnBranch (body: string) : Result<string, ProviderFailure> =
        try
            use doc = JsonDocument.Parse body
            let root = doc.RootElement
            let oid =
                prop "data" root
                |> Option.bind (prop "createCommitOnBranch")
                |> Option.bind (prop "commit")
                |> Option.bind (sha "oid")
            match oid, prop "errors" root with
            // GitHub names the commit it created: the save was applied, whatever else it reports (review 7, F7).
            | Some c, _ -> Ok c
            | _, Some errors when errors.ValueKind = JsonValueKind.Array && errors.GetArrayLength() > 0 ->
                let first = errors[0]
                let kind = str "type" first |> Option.defaultValue ""
                let text = str "message" first |> Option.defaultValue ""
                if kind = "STALE_DATA" || text.Contains("Expected branch to point to", StringComparison.Ordinal) then Error NotFastForward
                elif kind = "RATE_LIMITED" then Error(Forbidden true)
                elif kind = "FORBIDDEN" then Error(Forbidden(text.Contains("rate limit", StringComparison.OrdinalIgnoreCase)))
                elif kind = "NOT_FOUND" then Error NotFound
                elif kind = "UNPROCESSABLE" then Error(Rejected(200L, if text = "" then "GraphQL error" else text))
                // Any other error (untyped, INTERNAL, a timeout while executing) does not say nothing was applied (F3).
                else Error ProviderFailure.OutcomeUnknown
            | _ -> Error(Malformed "commit on branch")
        with _ -> Error(Malformed "commit on branch")

    /// The compare API's relation of head to base: "identical" or "ahead" means base is in head's history.
    let parseCompareStatus body = parse body (str "status") "comparison"

    /// A recursive tree listing; a truncated listing fails closed (we could not see every file).
    let parseTree body : Result<TreeEntry list, ProviderFailure> =
        parse body (fun e ->
            match flag "truncated" e, prop "tree" e with
            | Some true, _ -> None
            | _, Some items when items.ValueKind = JsonValueKind.Array ->
                items.EnumerateArray()
                |> Seq.map (fun item ->
                    let size = prop "size" item |> Option.bind (fun v -> if v.ValueKind = JsonValueKind.Number then Some(v.GetInt64()) else None)
                    match str "path" item, str "type" item, sha "sha" item with
                    | Some path, Some kind, Some s -> Some { Path = path; Kind = kind; Sha = s; Size = size }
                    | _ -> None)
                |> Seq.toList
                |> fun xs -> if xs |> List.forall Option.isSome then Some(List.choose id xs) else None
            | _ -> None) "tree (missing, malformed or truncated)"

    let parseBlob body : Result<byte[], ProviderFailure> =
        parse body (fun e ->
            match str "encoding" e, str "content" e with
            | Some "base64", Some content ->
                try Some(Convert.FromBase64String(content.Replace("\n", "").Replace("\r", ""))) with _ -> None
            | _ -> None) "blob"

    /// Git's object id for file bytes ("blob <n>\0" ‖ bytes, SHA-1): equal ids mean equal bytes.
    let blobSha (bytes: byte[]) =
        let header = Encoding.ASCII.GetBytes("blob " + string bytes.Length + "\000")
        Convert.ToHexStringLower(SHA1.HashData(Array.append header bytes))

    /// Group the tree under the investigations prefix by first path segment. Every entry counts (CONTRACT §3.3):
    /// a nested path appears as a subdirectory name so the reader rejects it.
    let investigations (prefix: string) (entries: TreeEntry list) : (string * (string * TreeEntry) list) list =
        entries
        |> List.filter (fun e -> e.Path.StartsWith(prefix, StringComparison.Ordinal) && e.Path.Length > prefix.Length)
        |> List.map (fun e -> e, e.Path.Substring(prefix.Length).Split('/'))
        |> List.filter (fun (e, segments) -> not (segments.Length = 1 && e.Kind = "tree"))
        |> List.groupBy (fun (_, segments) -> segments[0])
        |> List.map (fun (dir, items) ->
            dir,
            items
            |> List.filter (fun (_, segments) -> segments.Length >= 2)
            |> List.map (fun (e, segments) -> (if segments.Length = 2 then segments[1] else segments[1] + "/"), e)
            |> List.groupBy fst
            // A nested directory legitimately yields many "<name>/" entries; any other repeated name is refused.
            |> List.collect (fun (name, xs) ->
                match xs with
                | [ x ] -> [ x ]
                | x :: _ when name.EndsWith("/", StringComparison.Ordinal) -> [ x ]
                | _ -> xs |> List.mapi (fun i (_, e) -> name + "#duplicate-" + string i, e)))
        |> List.sortBy fst

    let describe (failure: ProviderFailure) =
        match failure with
        | Unauthorized -> "GitHub did not accept the credential (it may be expired or revoked)."
        | Forbidden true -> "GitHub refused the request because the rate limit was reached. Wait and retry."
        | Forbidden false -> "The credential is not permitted to perform this operation on this repository."
        | NotFound -> "The repository, branch or object was not found, or the credential cannot see it."
        | NotFastForward -> "The branch moved since it was read; GitHub applied nothing."
        | Rejected(status, _) -> "GitHub rejected the request (HTTP " + string status + ")."
        | Offline -> "The network is unavailable. Nothing was sent."
        | OutcomeUnknown -> "The connection dropped after the request was sent; the outcome is unknown."
        | Malformed what -> "GitHub returned an unexpected " + what + " response."
