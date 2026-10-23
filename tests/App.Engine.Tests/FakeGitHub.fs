/// An in-memory, immutable model of the parts of the GitHub Git Data REST API the engine uses. Blobs are keyed by
/// their real git object id (GitHub.blobSha); trees and commits by a deterministic 40-hex id. Every handler is a
/// pure function Request -> Repo -> status * body * Repo.
module App.Engine.Tests.FakeGitHub

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Causa.Engine

type CommitObj =
    { Tree: string
      Parents: string list
      Message: string }

type Repo =
    { Owner: string
      Name: string
      Branch: string
      Token: string
      Private: bool
      CanPush: bool
      Blobs: Map<string, byte[]>
      /// tree id -> (file path -> blob id); directories are derived when listing.
      Trees: Map<string, Map<string, string>>
      Commits: Map<string, CommitObj>
      Head: string }

type Request =
    { CorrelationId: string
      Method: string
      Url: string
      /// Path after /repos/{owner}/{repo}, including any query; None when the URL is for another repository.
      Route: string option
      Body: string option
      Authorization: string option }

let sha1Hex (s: string) = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes s))

let treeId (files: Map<string, string>) =
    sha1Hex ("tree\n" + (files |> Map.toList |> List.map (fun (p, s) -> p + " " + s) |> String.concat "\n"))

let commitId (c: CommitObj) = sha1Hex ("commit\n" + c.Tree + "\n" + String.concat "," c.Parents + "\n" + c.Message)

let private addBlob (bytes: byte[]) (repo: Repo) =
    let s = GitHub.blobSha bytes
    s, { repo with Blobs = repo.Blobs |> Map.add s bytes }

let private addTree (files: Map<string, string>) (repo: Repo) =
    let t = treeId files
    t, { repo with Trees = repo.Trees |> Map.add t files }

let private addCommit (c: CommitObj) (repo: Repo) =
    let id = commitId c
    id, { repo with Commits = repo.Commits |> Map.add id c }

let headCommit (repo: Repo) = repo.Commits[repo.Head]
let headFiles (repo: Repo) : Map<string, string> = repo.Trees[(headCommit repo).Tree]
let fileAtHead (repo: Repo) (path: string) : byte[] option = headFiles repo |> Map.tryFind path |> Option.map (fun s -> repo.Blobs[s])

/// Another writer commits on top of the current head and moves the branch (None deletes a path).
let commitFiles (message: string) (changes: (string * byte[] option) list) (repo: Repo) : Repo =
    let files, withBlobs =
        changes
        |> List.fold
            (fun (files: Map<string, string>, r) (path, content) ->
                match content with
                | Some bytes ->
                    let s, r' = addBlob bytes r
                    files |> Map.add path s, r'
                | None -> files |> Map.remove path, r)
            (headFiles repo, repo)
    let tree, withTree = addTree files withBlobs
    let commit, withCommit = addCommit { Tree = tree; Parents = [ repo.Head ]; Message = message } withTree
    { withCommit with Head = commit }

let create (owner: string) (name: string) (branch: string) (token: string) (files: (string * byte[]) list) : Repo =
    let empty =
        { Owner = owner; Name = name; Branch = branch; Token = token; Private = true; CanPush = true
          Blobs = Map.empty; Trees = Map.empty; Commits = Map.empty; Head = "" }
    let fileMap, withBlobs =
        files |> List.fold (fun (m: Map<string, string>, r) (p, b) -> let s, r' = addBlob b r in m |> Map.add p s, r') (Map.empty, empty)
    let tree, withTree = addTree fileMap withBlobs
    let commit, withCommit = addCommit { Tree = tree; Parents = []; Message = "seed" } withTree
    { withCommit with Head = commit }

/// Is `ancestor` reachable from `descendant` through parent links (inclusive)?
let isAncestor (repo: Repo) (ancestor: string) (descendant: string) =
    let rec walk (frontier: string list) (seen: Set<string>) =
        match frontier with
        | [] -> false
        | c :: _ when c = ancestor -> true
        | c :: rest when seen.Contains c -> walk rest seen
        | c :: rest ->
            let parents = repo.Commits |> Map.tryFind c |> Option.map (fun x -> x.Parents) |> Option.defaultValue []
            walk (rest @ parents) (seen.Add c)
    walk [ descendant ] Set.empty

let ancestors (repo: Repo) (commit: string) : string list =
    let rec go (c: string) acc =
        match repo.Commits |> Map.tryFind c with
        | Some { Parents = p :: _ } -> go p (p :: acc)
        | _ -> List.rev acc
    go commit []

// ---------------------------------------------------------------------------------------------------------------
// Responses
// ---------------------------------------------------------------------------------------------------------------

let private json (value: obj) = JsonSerializer.Serialize value
let private message (text: string) = json {| message = text; documentation_url = "https://docs.github.com/rest" |}
let private isSha (s: string) = s.Length = 40 && s |> Seq.forall (fun c -> Char.IsAsciiDigit c || (c >= 'a' && c <= 'f'))

/// The recursive listing GitHub returns: files and every intermediate directory, sorted by path.
let listing (files: Map<string, string>) =
    let dirs =
        files
        |> Map.toList
        |> List.collect (fun (p, _) ->
            let segs = p.Split '/'
            [ for i in 1 .. segs.Length - 1 -> String.Join("/", segs[0 .. i - 1]) ])
        |> List.distinct
    let dirEntry (d: string) =
        let inside = files |> Map.filter (fun p _ -> p.StartsWith(d + "/", StringComparison.Ordinal))
        {| path = d; mode = "040000"; ``type`` = "tree"; sha = sha1Hex ("dir\n" + d + "\n" + treeId inside) |}
    let fileEntry (p: string, s: string) = {| path = p; mode = "100644"; ``type`` = "blob"; sha = s |}
    (dirs |> List.map dirEntry) @ (files |> Map.toList |> List.map fileEntry)
    |> List.sortWith (fun a b -> String.CompareOrdinal(a.path, b.path))

let private parseBody (body: string option) : JsonElement option =
    match body with
    | None -> None
    | Some b ->
        try
            use doc = JsonDocument.Parse b
            Some(doc.RootElement.Clone())
        with _ -> None

let private str (name: string) (e: JsonElement) =
    match e.ValueKind with
    | JsonValueKind.Object ->
        match e.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.String -> Option.ofObj (v.GetString())
        | _ -> None
    | _ -> None

let private arr (name: string) (e: JsonElement) =
    match e.ValueKind with
    | JsonValueKind.Object ->
        match e.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.Array -> Some(v.EnumerateArray() |> Seq.toList)
        | _ -> None
    | _ -> None

let private flag (name: string) (e: JsonElement) =
    match e.ValueKind with
    | JsonValueKind.Object ->
        match e.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.True -> Some true
        | true, v when v.ValueKind = JsonValueKind.False -> Some false
        | _ -> None
    | _ -> None

let private repoJson (repo: Repo) =
    json
        {| full_name = repo.Owner + "/" + repo.Name
           ``private`` = repo.Private
           default_branch = repo.Branch
           permissions = {| admin = false; push = repo.CanPush; pull = true |} |}

let private prop (name: string) (e: JsonElement) =
    match e.ValueKind with
    | JsonValueKind.Object ->
        match e.TryGetProperty name with
        | true, v -> Some v
        | _ -> None
    | _ -> None

let private graphqlError (kind: string) (text: string) =
    json {| data = {| createCommitOnBranch = null |}; errors = [| {| ``type`` = kind; message = text |} |] |}

/// GitHub's GraphQL createCommitOnBranch: one atomic commit on the branch, applied only while the branch still names
/// expectedHeadOid (otherwise a STALE_DATA error in a 200 answer, as GitHub reports it).
let private commitOnBranch (req: Request) (repo: Repo) : int64 * string * Repo =
    let unchanged status body = status, body, repo
    let input = parseBody req.Body |> Option.bind (prop "variables") |> Option.bind (prop "input")
    let text name (e: JsonElement option) = e |> Option.bind (str name)
    match input with
    | None -> unchanged 200L (graphqlError "INVALID" "Variable $input was not provided")
    | Some i ->
        let branch = prop "branch" i
        let expected = str "expectedHeadOid" i
        let headline = text "headline" (prop "message" i)
        let body = text "body" (prop "message" i) |> Option.defaultValue ""
        let additions =
            prop "fileChanges" i
            |> Option.bind (arr "additions")
            |> Option.defaultValue []
            |> List.map (fun a -> str "path" a, str "contents" a)
        if text "repositoryNameWithOwner" branch <> Some(repo.Owner + "/" + repo.Name) || text "branchName" branch <> Some repo.Branch then
            unchanged 200L (graphqlError "NOT_FOUND" "Could not resolve to a Ref")
        elif not repo.CanPush then unchanged 200L (graphqlError "FORBIDDEN" "Resource not accessible by personal access token")
        elif expected <> Some repo.Head then
            unchanged 200L (graphqlError "STALE_DATA" (sprintf "Expected branch to point to \"%s\" but it did not. Pull and try again." (expected |> Option.defaultValue "")))
        elif headline.IsNone || additions.IsEmpty || additions |> List.exists (fun (p, c) -> p.IsNone || c.IsNone) then
            unchanged 200L (graphqlError "INVALID" "Invalid commit input")
        else
            let withBlobs, files =
                additions
                |> List.fold
                    (fun (r: Repo, m: Map<string, string>) (p, c) ->
                        let id, r' = addBlob (Convert.FromBase64String(Option.get c)) r
                        r', m |> Map.add (Option.get p) id)
                    (repo, repo.Trees[repo.Commits[repo.Head].Tree])
            let tree, withTree = addTree files withBlobs
            let message = Option.get headline + (if body = "" then "" else "\n\n" + body)
            let commit, withCommit = addCommit { Tree = tree; Parents = [ repo.Head ]; Message = message } withTree
            200L, json {| data = {| createCommitOnBranch = {| commit = {| oid = commit |} |} |} |}, { withCommit with Head = commit }

/// Answer one request. Returns status, JSON body and the repository after the request.
let handle (req: Request) (repo: Repo) : int64 * string * Repo =
    let unchanged status body = status, body, repo
    match req.Route with
    | None when req.Url = "https://api.github.com/graphql" && req.Method = "POST" ->
        if req.Authorization <> Some("Bearer " + repo.Token) then unchanged 401L (message "Bad credentials")
        else commitOnBranch req repo
    | None -> unchanged 404L (message "Not Found")
    | Some _ when req.Authorization <> Some("Bearer " + repo.Token) -> unchanged 401L (message "Bad credentials")
    | Some route ->
        let refPath = "/git/ref/heads/" + repo.Branch
        let refsPath = "/git/refs/heads/" + repo.Branch
        let writeDenied = req.Method <> "GET" && not repo.CanPush
        match req.Method, route with
        | _ when writeDenied -> unchanged 403L (message "Resource not accessible by personal access token")
        | "GET", "" -> unchanged 200L (repoJson repo)
        | "GET", r when r = refPath ->
            unchanged 200L (json {| ref = "refs/heads/" + repo.Branch; ``object`` = {| sha = repo.Head; ``type`` = "commit" |} |})
        | "GET", r when r.StartsWith("/git/commits/", StringComparison.Ordinal) ->
            match repo.Commits |> Map.tryFind (r.Substring "/git/commits/".Length) with
            | Some c -> unchanged 200L (json {| sha = commitId c; tree = {| sha = c.Tree |}; parents = c.Parents |> List.map (fun p -> {| sha = p |}); message = c.Message |})
            | None -> unchanged 404L (message "Not Found")
        | "GET", r when r.StartsWith("/git/trees/", StringComparison.Ordinal) && r.EndsWith("?recursive=1", StringComparison.Ordinal) ->
            let id = r.Substring("/git/trees/".Length, r.Length - "/git/trees/".Length - "?recursive=1".Length)
            match repo.Trees |> Map.tryFind id with
            | Some files -> unchanged 200L (json {| sha = id; tree = listing files; truncated = false |})
            | None -> unchanged 404L (message "Not Found")
        | "GET", r when r.StartsWith("/compare/", StringComparison.Ordinal) && r.Contains("...") ->
            // GitHub's compare API: the relation of head to base ("identical", "ahead", "behind", "diverged").
            let spec = r.Substring "/compare/".Length
            let i = spec.IndexOf "..."
            let b, h = spec.Substring(0, i), spec.Substring(i + 3)
            if not (repo.Commits.ContainsKey b && repo.Commits.ContainsKey h) then unchanged 404L (message "Not Found")
            else
                let status =
                    if b = h then "identical"
                    elif isAncestor repo b h then "ahead"
                    elif isAncestor repo h b then "behind"
                    else "diverged"
                unchanged 200L (json {| status = status |})
        | "GET", r when r.StartsWith("/git/blobs/", StringComparison.Ordinal) ->
            let id = r.Substring "/git/blobs/".Length
            match repo.Blobs |> Map.tryFind id with
            | Some bytes ->
                unchanged 200L (json {| sha = id; size = bytes.Length; encoding = "base64"; content = Convert.ToBase64String(bytes, Base64FormattingOptions.InsertLineBreaks) |})
            | None -> unchanged 404L (message "Not Found")
        | "POST", "/git/blobs" ->
            match parseBody req.Body |> Option.map (fun b -> str "content" b, str "encoding" b) with
            | Some(Some content, Some "base64") ->
                let s, r = addBlob (Convert.FromBase64String content) repo
                201L, json {| sha = s; url = "https://api.github.com/repos/x/y/git/blobs/" + s |}, r
            | _ -> unchanged 422L (message "Invalid request.")
        | "POST", "/git/trees" ->
            match parseBody req.Body with
            | Some b ->
                let baseFiles =
                    match str "base_tree" b with
                    | Some t -> repo.Trees |> Map.tryFind t
                    | None -> Some Map.empty
                let entries =
                    arr "tree" b
                    |> Option.defaultValue []
                    |> List.map (fun e -> str "path" e, str "type" e, str "mode" e, str "sha" e)
                let wellFormed =
                    entries
                    |> List.forall (function
                        | Some p, Some "blob", Some "100644", Some s -> isSha s && repo.Blobs.ContainsKey s && not (p.StartsWith "/") && not (p.Split('/') |> Array.contains "..")
                        | _ -> false)
                match baseFiles with
                | Some files when wellFormed ->
                    let merged = entries |> List.fold (fun (m: Map<string, string>) (p, _, _, s) -> m |> Map.add (Option.get p) (Option.get s)) files
                    let t, r = addTree merged repo
                    201L, json {| sha = t |}, r
                | _ -> unchanged 422L (message "Invalid tree info")
            | None -> unchanged 422L (message "Invalid request.")
        | "POST", "/git/commits" ->
            match parseBody req.Body with
            | Some b ->
                let parents = arr "parents" b |> Option.defaultValue [] |> List.map (fun p -> if p.ValueKind = JsonValueKind.String then p.GetString() |> Option.ofObj |> Option.defaultValue "" else "")
                match str "tree" b, str "message" b with
                | Some t, Some m when repo.Trees.ContainsKey t && parents |> List.forall repo.Commits.ContainsKey ->
                    let c, r = addCommit { Tree = t; Parents = parents; Message = m } repo
                    201L, json {| sha = c; tree = {| sha = t |} |}, r
                | _ -> unchanged 422L (message "Invalid commit")
            | None -> unchanged 422L (message "Invalid request.")
        | "PATCH", r when r = refsPath ->
            match parseBody req.Body with
            | Some b ->
                match str "sha" b with
                | Some s when repo.Commits.ContainsKey s ->
                    let force = flag "force" b |> Option.defaultValue false
                    if force || isAncestor repo repo.Head s then
                        let r' = { repo with Head = s }
                        200L, json {| ref = "refs/heads/" + repo.Branch; ``object`` = {| sha = s; ``type`` = "commit" |} |}, r'
                    else unchanged 422L (message "Update is not a fast forward")
                | _ -> unchanged 422L (message "Object does not exist")
            | None -> unchanged 422L (message "Invalid request.")
        | _ -> unchanged 404L (message "Not Found")

/// Interpret an engine Http URL against this repository.
let routeOf (repo: Repo) (url0: string) : string option =
    // The engine's cache-defeating parameter is not part of the route.
    let url = Text.RegularExpressions.Regex.Replace(url0, @"[?&]fresh=[^&]*$", "")
    let prefix = "https://api.github.com/repos/" + repo.Owner + "/" + repo.Name
    if url = prefix then Some ""
    elif url.StartsWith(prefix + "/", StringComparison.Ordinal) then Some(url.Substring prefix.Length)
    else None
