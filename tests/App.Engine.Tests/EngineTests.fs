/// Independent verification of the Causa engine against an in-memory GitHub. Every test drives the pure Limen
/// boundary (Shell.handle) and asserts what is observable: the App state, the projected ViewState, the effects
/// emitted and the fake repository afterwards.
module App.Engine.Tests.EngineTests

open System
open System.IO
open System.Text
open Xunit
open Limen.Contract.Core
open IndyInitRootCauseInvestigator.Engine
open Causa.Contract
open Causa.Engine
open App.Engine.Tests
open App.Engine.Tests.FakeGitHub
open App.Engine.Tests.Harness

// ---------------------------------------------------------------------------------------------------------------
// Scenario helpers
// ---------------------------------------------------------------------------------------------------------------

let private owner = "causa-test"
let private repoName = "causa-data-sandbox"
let private branch = "main"

/// A repository holding the five frozen investigations under `prefix`, plus an unrelated file.
let private repoWith (files: (string * byte[]) list) =
    FakeGitHub.create owner repoName branch token ((("README.md", Encoding.UTF8.GetBytes "sandbox\n")) :: files)

let private workspaceRepo (run: string) = repoWith (Fixtures.under (Fixtures.workspacePrefix run))
let private connected (root: string) (repo: Repo) = world repo |> init |> connect root

let private validIds (w: World) =
    (live w).Catalog |> List.filter (fun e -> match e.Status with Valid _ -> true | _ -> false) |> List.map (fun e -> e.Id) |> List.sort

let private invalidCode (w: World) (id: string) =
    match (entry id w).Status with
    | Invalid e -> e.Code
    | other -> failwithf "%s is not Invalid: %A" id other

let private createInvestigation (title: string) (observation: string) (w: World) =
    w |> openCommand "new-investigation" |> send "cmd.textA" title |> send "cmd.textB" observation |> fire "cmd.submit"

let private catalogIds (w: World) = (live w).Catalog |> List.map (fun e -> e.Id) |> Set.ofList

let private newId (before: World) (after: World) =
    match Set.difference (catalogIds after) (catalogIds before) |> Set.toList with
    | [ id ] -> id
    | other -> failwithf "expected exactly one new investigation, found %A" other

let private filesAt (repo: Repo) (prefix: string) (id: string) : Map<string, byte[]> =
    Store.fileNames
    |> List.choose (fun n -> fileAtHead repo (prefix + id + "/" + n) |> Option.map (fun b -> n, b))
    |> Map.ofList

let private validateAt (repo: Repo) (prefix: string) (id: string) =
    Store.ofFileMap id (filesAt repo prefix id) |> Result.bind (Store.validate Fixtures.schemaSet)

/// Another writer appends a domain-valid observation to an investigation and moves the branch.
let private otherWriterAppends (prefix: string) (id: string) (statement: string) : Repo -> Repo =
    fun repo ->
        let files = Store.ofFileMap id (filesAt repo prefix id) |> Result.defaultWith (fun e -> failwith e.Code)
        let loaded = Store.validate Fixtures.schemaSet files |> Result.defaultWith (fun e -> failwith e.Code)
        let envelope =
            { CommandId = Guid "5e1f0000-0000-4000-8000-000000000001"
              Investigation = loaded.State.Id
              ExpectedVersion = Some loaded.State.Version
              Actor = Actor.human "other-writer"
              RequestedAt = baseTime
              Command = RecordObservation(Id.create (Guid "5e1f0000-0000-4000-8000-000000000002"), statement, None) }
        let events = Decide.execute (Some loaded.State) envelope |> Result.defaultWith (fun es -> failwithf "%A" es)
        let next, _ = Store.append Fixtures.schemaSet loaded loaded.State.Version events |> Result.defaultWith (fun e -> failwith e.Code)
        repo |> commitFiles "other writer" (Fixtures.fileMapOf next |> List.map (fun (n, b) -> prefix + id + "/" + n, Some b))

/// Frozen investigations whose lifecycle is open (content may still be appended).
let private openIds =
    Fixtures.ids |> List.filter (fun id -> Transitions.isOpen (snd (Fixtures.loaded id)).State.Lifecycle)

let private patchBodiesNeverForce (w: World) =
    for r in w.Requests |> List.filter isPatch do
        let body = r.Body |> Option.defaultValue ""
        Assert.Contains("\"force\":false", body)
        Assert.DoesNotContain("\"force\":true", body)

let private sync (w: World) = (live w).Sync

// ---------------------------------------------------------------------------------------------------------------
// 1. Zero bundled data
// ---------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``1 a fresh engine projects a disconnected, empty workspace`` () =
    let w = world (workspaceRepo "unused") |> init
    match (lastResponse w).Handshake with
    | Some(EngineHandshake.Accepted _) -> ()
    | other -> failwithf "handshake not accepted: %A" other
    Assert.Equal(Disconnected, w.App.Connection)
    Assert.True(flag "isDisconnected" w)
    Assert.False(flag "isConnected" w)
    Assert.Equal("disconnected", text "connState" w)
    Assert.Empty(items "investigations" w)
    Assert.Equal(0.0, number "dashCount" w)
    Assert.Equal(0.0, number "dashValid" w)
    Assert.False(flag "canCreate" w)
    Assert.Empty((lastResponse w).Effects)

[<Fact>]
let ``1b the engine assemblies contain no frozen investigation ids or titles`` () =
    let titles = [ "Card payments failing at checkout"; "Users unable to sign in via single sign-on" ]
    let needles = Fixtures.ids @ titles
    let assemblies =
        [ typeof<App>.Assembly; typeof<InvestigationFiles>.Assembly; typeof<Investigation>.Assembly ]
        |> List.map (fun a -> a.Location)
    for path in assemblies do
        let bytes = File.ReadAllBytes path
        let utf8 = Encoding.UTF8.GetString bytes
        let utf16 = Encoding.Unicode.GetString bytes
        for n in needles do
            Assert.False(utf8.Contains n || utf16.Contains n, sprintf "%s embeds %s" path n)

// ---------------------------------------------------------------------------------------------------------------
// 2. Explicit target and credential
// ---------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``2 connect without a credential emits no request and explains why`` () =
    let w = world (workspaceRepo "r2") |> init |> connectAs owner repoName branch "r2" ""
    Assert.Empty((lastResponse w).Effects)
    Assert.Empty(w.Requests)
    Assert.Equal(Disconnected, w.App.Connection)
    Assert.True(flag "hasNotice" w)
    Assert.Contains("credential", (text "noticeText" w).ToLowerInvariant())

[<Fact>]
let ``2b connect requires owner, repository, branch and workspace explicitly`` () =
    let attempts =
        [ "", repoName, branch, "r2"
          owner, "", branch, "r2"
          owner, repoName, "", "r2"
          owner, repoName, branch, "" ]
    for (o, r, b, root) in attempts do
        let w = world (workspaceRepo "r2") |> init |> connectAs o r b root token
        Assert.Empty(w.Requests)
        Assert.Equal(Disconnected, w.App.Connection)
        Assert.True(flag "hasNotice" w)
        Assert.False(w.App.ConnectForm.ContainsKey "token", "a refused attempt must not keep the credential in the form")

// ---------------------------------------------------------------------------------------------------------------
// 3. Authorized read
// ---------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``3 connecting to the frozen fixtures loads all five as Valid without mutating anything`` () =
    let repo = repoWith (Fixtures.under Fixtures.fixturesPrefix)
    let w = connected "fixtures" repo
    Assert.Equal<string list>(Fixtures.ids, validIds w)
    Assert.Equal(5.0, number "dashCount" w)
    Assert.Equal(5.0, number "dashValid" w)
    Assert.Equal(0.0, number "dashInvalid" w)
    Assert.Equal("connected", text "connState" w)
    Assert.True(flag "isConnected" w)
    Assert.Equal(5, items "investigations" w |> List.length)
    Assert.All(w.Requests, fun r -> Assert.Equal("GET", r.Method))
    Assert.Equal(repo.Head, w.Repo.Head)
    let first = List.take 4 w.Requests |> List.map shape
    Assert.Equal<string list>([ "GET repo"; "GET ref"; "GET commit"; "GET tree" ], first)

[<Fact>]
let ``3b the same five load from a workspaces demo run root`` () =
    let w = connected "run-3b" (workspaceRepo "run-3b")
    Assert.Equal<string list>(Fixtures.ids, validIds w)
    Assert.Equal(5.0, number "dashValid" w)
    Assert.True(flag "canCreate" w)

[<Fact>]
let ``3c a workspace root does not see the fixtures root or another run`` () =
    let repo = repoWith (Fixtures.under Fixtures.fixturesPrefix @ Fixtures.under (Fixtures.workspacePrefix "other"))
    let w = connected "mine" repo
    Assert.Empty((live w).Catalog)
    Assert.True(flag "dashEmpty" w)
    Assert.Equal(0.0, number "dashCount" w)

// ---------------------------------------------------------------------------------------------------------------
// 4. Denial
// ---------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``4 a 401 on the repository check is refused as access denied with no catalog`` () =
    let w = world (workspaceRepo "r4") |> init |> connectAs owner repoName branch "r4" "ghs_wrong_token"
    match w.App.Connection with
    | Refused(_, AccessDenied _) -> ()
    | other -> failwithf "expected Refused AccessDenied, got %A" other
    Assert.Equal("unauthorized", text "connState" w)
    Assert.True(flag "isRefused" w)
    Assert.Empty(items "investigations" w)
    Assert.Equal(0.0, number "dashCount" w)
    Assert.Equal<string list>([ "GET repo" ], w.Requests |> List.map shape)

[<Fact>]
let ``4b 403, 404 and network failures on connect are distinct refusals`` () =
    let cases =
        [ Respond(403L, """{"message":"Resource not accessible by personal access token"}"""), "unauthorized"
          Respond(404L, """{"message":"Not Found"}"""), "unauthorized"
          NetworkDown, "offline" ]
    for action, state in cases do
        let w = world (workspaceRepo "r4") |> withRule (fun r -> r.Route = Some "") action |> init |> connect "r4"
        Assert.Equal(state, text "connState" w)
        Assert.Empty(items "investigations" w)
        match w.App.Connection, action with
        | Refused(_, AccessDenied _), Respond(403L, _) -> ()
        | Refused(_, NotFoundRefusal _), Respond(404L, _) -> ()
        | Refused(_, OfflineRefusal _), NetworkDown -> ()
        | other, _ -> failwithf "unexpected connection %A" other

[<Fact>]
let ``4c a 401 on the branch read after a good repository check is also refused`` () =
    let w = world (workspaceRepo "r4") |> withRule (fun r -> r.Route |> Option.exists (fun p -> p.StartsWith "/git/ref/")) (Respond(401L, """{"message":"Bad credentials"}""")) |> init |> connect "r4"
    match w.App.Connection with
    | Refused(_, AccessDenied _) -> ()
    | other -> failwithf "expected AccessDenied, got %A" other
    Assert.Empty(items "investigations" w)

// ---------------------------------------------------------------------------------------------------------------
// 5–6. Malformed and forged data fail closed
// ---------------------------------------------------------------------------------------------------------------

let private tamper (bytes: byte[]) =
    let s = Encoding.UTF8.GetString bytes
    let i = s.IndexOf("\"title\":\"", StringComparison.Ordinal) + 9
    let copy = Array.copy bytes
    copy[i] <- (if copy[i] = byte 'X' then byte 'Y' else byte 'X')
    copy

[<Fact>]
let ``5 one tampered events file makes only that investigation Invalid and it is never shown`` () =
    let victim = Fixtures.ids.Head
    let prefix = Fixtures.workspacePrefix "r5"
    let files =
        Fixtures.under prefix
        |> List.map (fun (p, b) -> if p = prefix + victim + "/events.ndjson" then p, tamper b else p, b)
    let w = connected "r5" (repoWith files)
    let code = invalidCode w victim
    Assert.StartsWith("CAUSA.", code)
    Assert.Equal<string list>(Fixtures.ids.Tail, validIds w)
    Assert.Equal(4.0, number "dashValid" w)
    Assert.Equal(1.0, number "dashInvalid" w)
    let row = items "investigations" w |> List.find (fun r -> itemText "id" r = victim)
    Assert.Equal("invalid", itemText "status" row)
    Assert.StartsWith(code, itemText "errorCode" row)
    let opened = w |> goTo ("#/i/" + victim + "/overview")
    Assert.True(flag "invMissing" opened)
    Assert.False(flag "hasInvestigation" opened)
    Assert.Equal("", text "invTitle" opened)
    Assert.Empty(items "timeline" opened)
    Assert.Contains(code, text "invMissingText" opened)

[<Fact>]
let ``6 extra files, nested paths, stray files and non-UUID directories are CAUSA.PATH_INVALID`` () =
    let prefix = Fixtures.workspacePrefix "r6"
    let a, b = Fixtures.ids[0], Fixtures.ids[1]
    let extra =
        [ prefix + a + "/notes.txt", Encoding.UTF8.GetBytes "extra"
          prefix + b + "/sub/evil.json", Encoding.UTF8.GetBytes "{}"
          prefix + "README.md", Encoding.UTF8.GetBytes "stray"
          prefix + Fixtures.ids[2].ToUpperInvariant() + "/x.json", Encoding.UTF8.GetBytes "{}" ]
    let notUuid = Fixtures.files Fixtures.ids[3] |> List.map (fun (n, bytes) -> prefix + "not-a-uuid/" + n, bytes)
    let w = connected "r6" (repoWith (Fixtures.under prefix @ extra @ notUuid))
    for id in [ a; b; "README.md"; "not-a-uuid"; Fixtures.ids[2].ToUpperInvariant() ] do
        Assert.Equal(Codes.pathInvalid, invalidCode w id)
    Assert.Equal<string list>(Fixtures.ids |> List.skip 2, validIds w)
    Assert.Equal(3.0, number "dashValid" w)
    Assert.Equal(5.0, number "dashInvalid" w)
    let opened = w |> goTo ("#/i/" + a + "/overview")
    Assert.True(flag "invMissing" opened)
    Assert.Contains(Codes.pathInvalid, text "invMissingText" opened)

// ---------------------------------------------------------------------------------------------------------------
// 7–8. Create
// ---------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``7 creating an investigation publishes exactly three files, verifies by read-back and leaves other paths intact`` () =
    let run = "r7"
    let prefix = Fixtures.workspacePrefix run
    let before = connected run (workspaceRepo run)
    let baseHead = before.Repo.Head
    let baseFiles = headFiles before.Repo
    let n = before.Requests.Length
    let opened = before |> openCommand "new-investigation" |> send "cmd.textA" "Checkout latency spike" |> send "cmd.textB" "p99 latency rose to 4s at 09:12 UTC"
    let submitted = opened |> deliver (event "cmd.submit" None None)
    let firstEffects = httpEffects (lastResponse submitted)
    Assert.Equal(3, firstEffects.Length)
    Assert.All(firstEffects, fun h -> Assert.Equal(HttpMethod.Post, h.Method); Assert.EndsWith("/git/blobs", h.Url))
    let after = pump submitted
    let sequence = requestsSince n after |> List.map shape
    Assert.Equal<string list>(
        [ "POST blob"; "POST blob"; "POST blob"; "POST tree"; "POST commit"; "PATCH ref"
          "GET ref"; "GET commit"; "GET tree"; "GET blob"; "GET blob"; "GET blob" ],
        sequence)
    let id = newId before after
    let l = live after
    Assert.Equal(Idle, l.Sync)
    Assert.Equal(Some after.Repo.Head, l.LastVerified |> Option.map fst)
    Assert.Equal(after.Repo.Head, l.Head.Commit)
    match (entry id after).Status with
    | Valid(_, loaded) -> Assert.Equal("Checkout latency spike", loaded.State.Title.Value)
    | other -> failwithf "new entry not valid: %A" other
    Assert.Equal(6.0, number "dashValid" after)
    Assert.Equal("synced", text "syncState" after)
    Assert.Equal(InvestigationRoute(id, Overview, None), after.App.Route)
    // The repository: one fast-forward commit on top of the base; exactly three new paths; nothing else changed.
    Assert.NotEqual<string>(baseHead, after.Repo.Head)
    Assert.Equal<string list>([ baseHead ], (headCommit after.Repo).Parents)
    let newFiles = headFiles after.Repo
    let added = newFiles |> Map.toList |> List.map fst |> List.filter (fun p -> not (baseFiles.ContainsKey p)) |> List.sort
    Assert.Equal<string list>(Store.fileNames |> List.map (fun f -> prefix + id + "/" + f) |> List.sort, added)
    Assert.Equal(baseFiles.Count + 3, newFiles.Count)
    for KeyValue(p, s) in baseFiles do
        Assert.Equal(s, newFiles[p])
    // Independent re-validation of the bytes GitHub now holds.
    match validateAt after.Repo prefix id with
    | Ok loaded ->
        Assert.Equal("Checkout latency spike", loaded.State.Title.Value)
        Assert.Contains(loaded.State.Observations |> Map.toList |> List.map (fun (_, o) -> o.Original.Value), fun t -> t = "p99 latency rose to 4s at 09:12 UTC")
        Assert.Equal(Ids.text loaded.InvestigationId, id)
    | Error e -> failwithf "written files do not validate: %s %s" e.Code e.Detail
    patchBodiesNeverForce after

[<Fact>]
let ``8 the frozen fixtures root offers no create and refuses a submitted one`` () =
    let w = connected "fixtures" (repoWith (Fixtures.under Fixtures.fixturesPrefix))
    Assert.False(flag "canCreate" w)
    Assert.NotEqual<string>("", text "createReason" w)
    let n = w.Requests.Length
    let opened = w |> openCommand "new-investigation" |> send "cmd.textA" "T" |> send "cmd.textB" "O"
    let submitted = opened |> deliver (event "cmd.submit" None None)
    Assert.Empty(httpEffects (lastResponse submitted))
    let after = pump submitted
    Assert.True(flag "hasDialogError" after)
    Assert.Contains("read-only", text "dialogError" after)
    Assert.Empty(requestsSince n after)
    Assert.Equal(Idle, sync after)

// ---------------------------------------------------------------------------------------------------------------
// 9. Optimistic concurrency
// ---------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``9 a concurrent change to a different investigation is rebased onto, not overwritten`` () =
    let run = "r9"
    let prefix = Fixtures.workspacePrefix run
    let other = openIds[1]
    let w0 = connected run (workspaceRepo run) |> withRule isPatch (Concurrent(otherWriterAppends prefix other "seen by the other writer"))
    let after = w0 |> createInvestigation "Rebased" "first observation"
    let id = newId w0 after
    Assert.Equal(Idle, sync after)
    Assert.Equal("synced", text "syncState" after)
    Assert.Equal(2, after.Requests |> List.filter isPatch |> List.length)
    // The other writer's commit is an ancestor of the final head: its history was preserved.
    let concurrentCommit = (headCommit after.Repo).Parents.Head
    Assert.Equal("other writer", after.Repo.Commits[concurrentCommit].Message)
    match validateAt after.Repo prefix other with
    | Ok l -> Assert.Contains(l.State.Observations |> Map.toList |> List.map (fun (_, o) -> o.Original.Value), fun t -> t = "seen by the other writer")
    | Error e -> failwithf "other investigation damaged: %s" e.Code
    Assert.True(Result.isOk (validateAt after.Repo prefix id))
    patchBodiesNeverForce after

let private observe (id: string) (statement: string) (w: World) =
    w |> goTo ("#/i/" + id + "/overview") |> openCommand "add-observation" |> send "cmd.textB" statement |> fire "cmd.submit"

[<Fact>]
let ``9b a concurrent change to the same investigation is held as a conflict and never overwritten`` () =
    let run = "r9b"
    let prefix = Fixtures.workspacePrefix run
    let target = openIds[0]
    let w0 = connected run (workspaceRepo run) |> withRule isPatch (Concurrent(otherWriterAppends prefix target "the other writer's observation"))
    let after = w0 |> observe target "my observation"
    match sync after with
    | Held(_, Conflict _) -> ()
    | other -> failwithf "expected Held Conflict, got %A" other
    Assert.Equal("conflict", text "syncState" after)
    Assert.True(flag "canReapply" after)
    Assert.Equal("other writer", (headCommit after.Repo).Message)
    Assert.Equal(1, after.Requests |> List.filter isPatch |> List.length)
    match validateAt after.Repo prefix target with
    | Ok l ->
        let texts = l.State.Observations |> Map.toList |> List.map (fun (_, o) -> o.Original.Value)
        Assert.Contains("the other writer's observation", texts)
        Assert.DoesNotContain("my observation", texts)
    | Error e -> failwithf "remote investigation damaged: %s" e.Code
    patchBodiesNeverForce after

[<Fact>]
let ``9c reapplying a conflicted change re-decides it on top of the remote history`` () =
    let run = "r9c"
    let prefix = Fixtures.workspacePrefix run
    let target = openIds[0]
    let held = connected run (workspaceRepo run) |> withRule isPatch (Concurrent(otherWriterAppends prefix target "the other writer's observation")) |> observe target "my observation"
    let conflictHead = held.Repo.Head
    let after = held |> fire "sync.reapply"
    Assert.Equal(Idle, sync after)
    Assert.True(FakeGitHub.isAncestor after.Repo conflictHead after.Repo.Head)
    match validateAt after.Repo prefix target with
    | Ok l ->
        let texts = l.State.Observations |> Map.toList |> List.map (fun (_, o) -> o.Original.Value)
        Assert.Contains("the other writer's observation", texts)
        Assert.Contains("my observation", texts)
    | Error e -> failwithf "reapplied investigation invalid: %s %s" e.Code e.Detail
    patchBodiesNeverForce after

// ---------------------------------------------------------------------------------------------------------------
// 10. Offline
// ---------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``10 offline during blob creation holds the change locally and retry publishes it`` () =
    let run = "r10"
    let prefix = Fixtures.workspacePrefix run
    let w0 = connected run (workspaceRepo run) |> withRule isPostBlob NetworkDown
    let baseHead = w0.Repo.Head
    let held = w0 |> createInvestigation "Offline" "written while offline"
    match sync held with
    | Held(_, NotSent _) -> ()
    | other -> failwithf "expected Held NotSent, got %A" other
    Assert.Equal("queued", text "syncState" held)
    Assert.True(flag "canRetry" held)
    Assert.Equal(baseHead, held.Repo.Head)
    Assert.Empty(held.Requests |> List.filter isPatch)
    let retried = held |> fire "sync.retry"
    Assert.Equal(Idle, sync retried)
    Assert.Equal("synced", text "syncState" retried)
    let w = live retried
    Assert.Equal(Some retried.Repo.Head, w.LastVerified |> Option.map fst)
    let id = newId w0 retried
    Assert.True(Result.isOk (validateAt retried.Repo prefix id))

// ---------------------------------------------------------------------------------------------------------------
// 11. Outcome unknown
// ---------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``11 an unknown PATCH outcome that did apply is reconciled by reading the ref, then verified`` () =
    let run = "r11"
    let w0 = connected run (workspaceRepo run) |> withRule isPatch (UnknownAfter true)
    let n = w0.Requests.Length
    let after = w0 |> createInvestigation "Unknown" "the response was lost"
    let sequence = requestsSince n after |> List.map shape
    Assert.Equal<string list>(
        [ "POST blob"; "POST blob"; "POST blob"; "POST tree"; "POST commit"; "PATCH ref"
          "GET ref"; "GET commit"; "GET tree"; "GET blob"; "GET blob"; "GET blob" ],
        sequence)
    Assert.Equal(Idle, sync after)
    Assert.Equal(Some after.Repo.Head, (live after).LastVerified |> Option.map fst)
    // Success is projected only on the response to the final read-back.
    let total = after.Responses.Length
    let synced = after.Responses |> List.map (fun r -> r.View |> Map.tryFind "syncState") |> List.mapi (fun i s -> i, s) |> List.filter (fun (_, s) -> s = Some(ViewValue.Text "synced"))
    Assert.Equal(total - 1, fst synced.Head)

[<Fact>]
let ``11b an unknown PATCH outcome that did not apply is retried as a fast-forward`` () =
    let run = "r11b"
    let w0 = connected run (workspaceRepo run) |> withRule isPatch (UnknownAfter false)
    let baseHead = w0.Repo.Head
    let after = w0 |> createInvestigation "Unknown" "the request never arrived"
    Assert.Equal(Idle, sync after)
    Assert.Equal(2, after.Requests |> List.filter isPatch |> List.length)
    Assert.Equal<string list>([ baseHead ], (headCommit after.Repo).Parents)
    patchBodiesNeverForce after

[<Fact>]
let ``11c a read-back that differs from what was written is never reported as success`` () =
    let run = "r11c"
    let bogus = Convert.ToBase64String(Encoding.UTF8.GetBytes "{}")
    let w0 = connected run (workspaceRepo run)
    let n = w0.Requests.Length
    // Only the verification read (after the PATCH) is corrupted.
    let w1 = { w0 with Rules = [] } |> withRule (fun r -> isGetBlob r && r.CorrelationId <> "") (Respond(200L, sprintf """{"sha":"x","encoding":"base64","content":"%s"}""" bogus))
    let after = w1 |> createInvestigation "Corrupt" "read-back tampered"
    Assert.True(requestsSince n after |> List.exists isPatch)
    // After the PATCH a mismatch is not a definite failure: the branch moved, so the outcome is unknown (DC-003).
    match sync after with
    | Held(_, Unknown _) -> ()
    | other -> failwithf "expected Held Unknown, got %A" other
    Assert.Equal(None, (live after).LastVerified)
    Assert.Equal("unknown", text "syncState" after)
    Assert.False(flag "canRetry" after)
    Assert.True(flag "canReconcile" after)

// ---------------------------------------------------------------------------------------------------------------
// 12. Credential confinement
// ---------------------------------------------------------------------------------------------------------------

let private redact (m: EngineToBrowserMessage) =
    { m with
        Effects =
            m.Effects
            |> List.map (function
                | EffectRequest.Http h ->
                    EffectRequest.Http { h with Headers = h.Headers |> Option.map (Map.map (fun k v -> if k = "Authorization" then "REDACTED" else v)) }
                | e -> e) }

[<Fact>]
let ``12 the credential appears only in Authorization headers and is discarded on disconnect`` () =
    let run = "r12"
    let target = openIds[0]
    let session =
        world (workspaceRepo run)
        |> init
        |> connectAs owner repoName branch run "ghs_wrong_token" // refused attempt with a different secret
        |> connect run
        |> createInvestigation "Credential" "observation"
        |> withRule isPatch (Concurrent(otherWriterAppends (Fixtures.workspacePrefix run) target "other"))
        |> observe target "conflicting"
        |> withRule isPostBlob NetworkDown
        |> fire "sync.discard"
        |> observe target "offline"
        |> openCommand "resolve"
        |> fire "cmd.submit"
        |> fire "cmd.submit"
        |> fire "disconnect"
    let mutable authorized = 0
    for m in session.Responses do
        for h in httpEffects m do
            authorized <- authorized + 1
            let auth = h.Headers |> Option.bind (Map.tryFind "Authorization")
            Assert.True(auth = Some("Bearer " + token) || auth = Some "Bearer ghs_wrong_token", sprintf "unexpected Authorization %A" auth)
            Assert.DoesNotContain(token, h.Url)
            Assert.DoesNotContain(token, h.Body |> Option.defaultValue "")
        let json = Codec.serializeEngineToBrowserMessage (redact m)
        Assert.DoesNotContain(token, json)
        Assert.DoesNotContain("ghs_wrong_token", json)
        Assert.DoesNotContain(token, sprintf "%A" m.View)
    Assert.True(authorized > 20)
    Assert.Equal(Disconnected, session.App.Connection)
    Assert.Empty(session.App.Inflight)
    Assert.False(session.App.ConnectForm.ContainsKey "token")
    Assert.DoesNotContain(token, sprintf "%A" session.App)
    Assert.True(flag "isDisconnected" session)
    Assert.Empty(items "investigations" session)

[<Fact>]
let ``12b disconnect cancels in-flight requests and ignores their late answers`` () =
    let run = "r12b"
    let connectedWorld = connected run (workspaceRepo run)
    let writing =
        connectedWorld |> openCommand "new-investigation" |> send "cmd.textA" "T" |> send "cmd.textB" "O"
        |> deliver (event "cmd.submit" None None)
    let inflight = writing.App.Inflight |> Map.toList |> List.map fst |> Set.ofList
    Assert.Equal(3, inflight.Count)
    let disconnected = writing |> deliver (event "disconnect" None None)
    let cancelled = (lastResponse disconnected).Cancellations |> List.map (fun (CorrelationId c) -> c) |> Set.ofList
    Assert.Equal<Set<string>>(inflight, cancelled)
    Assert.Equal(Disconnected, disconnected.App.Connection)
    Assert.Empty(disconnected.App.Inflight)
    // The host may still deliver results for cancelled requests; they must change nothing.
    let late = pump disconnected
    Assert.Equal(Disconnected, late.App.Connection)
    Assert.All(late.Responses |> List.skip disconnected.Responses.Length, fun r -> Assert.Empty(r.Effects))
    Assert.Equal(connectedWorld.Repo.Head, late.Repo.Head)
    // Same during connect.
    let connecting = world (workspaceRepo run) |> init |> fillConnect owner repoName branch run token |> deliver (event "connect.submit" None None)
    let dropped = connecting |> deliver (event "connect.cancel" None None)
    Assert.Equal(1, (lastResponse dropped).Cancellations.Length)
    let lateConnect = pump dropped
    Assert.Equal(Disconnected, lateConnect.App.Connection)
    Assert.DoesNotContain(token, sprintf "%A" lateConnect.App)

// ---------------------------------------------------------------------------------------------------------------
// 13. Domain authority
// ---------------------------------------------------------------------------------------------------------------

let private envelopeFor (loaded: Loaded) (command: Command) =
    { CommandId = Guid "d0d0d0d0-0000-4000-8000-000000000001"
      Investigation = loaded.State.Id
      ExpectedVersion = Some loaded.State.Version
      Actor = Session.actor
      RequestedAt = baseTime
      Command = command }

let private assertDomainRefusal (expectedCode: string) (opened: World) =
    let n = opened.Requests.Length
    let confirming = opened |> fire "cmd.submit"
    let decided = confirming |> deliver (event "cmd.submit" None None)
    Assert.Empty(httpEffects (lastResponse decided))
    let after = pump decided
    Assert.True(flag "hasDialogError" after)
    let error = text "dialogError" after
    Assert.StartsWith("The domain refused this command", error)
    Assert.Contains("(" + expectedCode + ")", error)
    Assert.Empty(requestsSince n after |> List.filter isWrite)
    Assert.Equal(Idle, sync after)

[<Fact>]
let ``13 resolve refused by the domain shows the refusal and writes nothing`` () =
    let candidate =
        Fixtures.ids
        |> List.tryPick (fun id ->
            let _, l = Fixtures.loaded id
            if not (Transitions.isOpen l.State.Lifecycle) then None
            else
                match Decide.execute (Some l.State) (envelopeFor l ResolveInvestigation) with
                | Error(e :: _) -> Some(id, e.Code)
                | _ -> None)
    let id, code = candidate |> Option.defaultWith (fun () -> failwith "no open fixture where the domain refuses resolve")
    let run = "r13"
    connected run (workspaceRepo run) |> goTo ("#/i/" + id + "/overview") |> openCommand "resolve" |> assertDomainRefusal code

[<Fact>]
let ``13b confirm-cause refused by the cause gate shows the refusal and writes nothing`` () =
    let candidate =
        Fixtures.ids
        |> List.tryPick (fun id ->
            let _, l = Fixtures.loaded id
            if not (Transitions.isOpen l.State.Lifecycle) then None
            else
                l.State.Causes
                |> Map.toList
                |> List.tryPick (fun (cid, _) ->
                    match Decide.execute (Some l.State) (envelopeFor l (ConfirmCause cid)) with
                    | Error(e :: _) -> Some(id, Ids.text cid.Value, e.Code)
                    | _ -> None))
    let id, cause, code = candidate |> Option.defaultWith (fun () -> failwith "no fixture cause the domain refuses to confirm")
    let run = "r13b"
    connected run (workspaceRepo run) |> goTo ("#/i/" + id + "/causes/" + cause) |> openCommand "confirm-cause" |> assertDomainRefusal code

// ---------------------------------------------------------------------------------------------------------------
// 14. Stale or forged history through the contract path
// ---------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``14 appendLines with a stale expected version is CAUSA.VERSION_STALE; a forged line is refused`` () =
    let files, loaded = Fixtures.loaded openIds[0]
    let envelope =
        { envelopeFor loaded (RecordObservation(Id.create (Guid "a11e0000-0000-4000-8000-000000000001"), "appended", None)) with
            CommandId = Guid "a11e0000-0000-4000-8000-000000000002" }
    let events = Decide.execute (Some loaded.State) envelope |> Result.defaultWith (fun es -> failwithf "%A" es)
    let next, _ = Store.append Fixtures.schemaSet loaded loaded.State.Version events |> Result.defaultWith (fun e -> failwith e.Code)
    let appended = next.Events |> Array.skip files.Events.Length
    Assert.True(Result.isOk (Store.appendLines Fixtures.schemaSet loaded loaded.State.Version appended))
    match Store.appendLines Fixtures.schemaSet loaded (loaded.State.Version - 1L) appended with
    | Error e -> Assert.Equal(Codes.versionStale, e.Code)
    | Ok _ -> failwith "a stale append was accepted"
    match Store.append Fixtures.schemaSet loaded (loaded.State.Version - 1L) events with
    | Error e -> Assert.Equal(Codes.versionStale, e.Code)
    | Ok _ -> failwith "a stale append was accepted"
    let v = loaded.State.Version
    let forged =
        Encoding.UTF8.GetBytes((Encoding.UTF8.GetString appended).Replace(sprintf "\"aggregateVersion\":%d," (v + 1L), sprintf "\"aggregateVersion\":%d," (v + 3L)))
    Assert.NotEqual<byte[]>(appended, forged)
    match Store.appendLines Fixtures.schemaSet loaded loaded.State.Version forged with
    | Error e -> Assert.StartsWith("CAUSA.", e.Code)
    | Ok _ -> failwith "a forged append was accepted"

// ---------------------------------------------------------------------------------------------------------------
// Further probes of the concurrency and reconciliation paths
// ---------------------------------------------------------------------------------------------------------------

let private observations (repo: Repo) (prefix: string) (id: string) =
    match validateAt repo prefix id with
    | Ok l -> l.State.Observations |> Map.toList |> List.map (fun (_, o) -> o.Original.Value)
    | Error e -> failwithf "remote %s invalid: %s %s" id e.Code e.Detail

[<Fact>]
let ``9d after rebasing over another writer, a later write to the investigation that writer changed does not erase its history`` () =
    let run = "r9d"
    let prefix = Fixtures.workspacePrefix run
    let y = openIds[1]
    let w0 = connected run (workspaceRepo run) |> withRule isPatch (Concurrent(otherWriterAppends prefix y "other writer on Y"))
    let created = w0 |> createInvestigation "Rebased first" "observation"
    Assert.Equal(Idle, sync created)
    Assert.Contains("other writer on Y", observations created.Repo prefix y)
    let after = created |> observe y "my later observation on Y"
    // Whatever the engine decides (conflict, reload, re-decision), unread remote history must never be overwritten.
    Assert.Contains("other writer on Y", observations after.Repo prefix y)
    patchBodiesNeverForce after

[<Fact>]
let ``10b retrying a held change after a reload does not overwrite a concurrent change to the same investigation`` () =
    let run = "r10b"
    let prefix = Fixtures.workspacePrefix run
    let x = openIds[0]
    let held = connected run (workspaceRepo run) |> withRule isPostBlob NetworkDown |> observe x "mine, written offline"
    match sync held with
    | Held(_, NotSent _) -> ()
    | other -> failwithf "expected Held NotSent, got %A" other
    Assert.True(flag "canRefresh" held)
    let moved = { held with Repo = otherWriterAppends prefix x "other writer meanwhile" held.Repo }
    let after = moved |> fire "refresh" |> fire "sync.retry"
    Assert.Contains("other writer meanwhile", observations after.Repo prefix x)
    patchBodiesNeverForce after

[<Fact>]
let ``11d an unknown PATCH outcome that applied, with the branch moved on afterwards, is not reported as an unwritten conflict`` () =
    let run = "r11d"
    let prefix = Fixtures.workspacePrefix run
    let notes = "workspaces/demo/" + run + "/notes.txt"
    let w0 =
        connected run (workspaceRepo run)
        |> withRule isPatch (UnknownAfter true)
        |> withRule (fun r -> r.Method = "GET" && (r.Route |> Option.exists (fun p -> p.StartsWith "/git/ref/")))
            (Concurrent(commitFiles "later writer" [ notes, Some(Encoding.UTF8.GetBytes "later") ]))
    let after = w0 |> createInvestigation "Applied" "then the branch moved"
    let id = Set.difference (headFiles after.Repo |> Map.toList |> List.map fst |> List.filter (fun p -> p.StartsWith prefix) |> List.map (fun p -> p.Substring(prefix.Length).Split('/')[0]) |> Set.ofList) (Set.ofList Fixtures.ids) |> Set.toList |> List.exactlyOne
    // The write is on the branch (its commit is an ancestor of the head) ...
    Assert.True(Result.isOk (validateAt after.Repo prefix id))
    // ... so the engine must not tell the user it was not written.
    match sync after with
    | Held(_, Conflict detail) -> failwithf "a write that is on the branch was reported as a conflict: %s" detail
    | Held(_, other) -> failwithf "write held: %A" other
    | InFlight _ -> failwith "write never settled"
    | Idle -> Assert.True((live after).LastVerified.IsSome)

[<Fact>]
let ``4d a truncated tree listing fails closed as incompatible`` () =
    let w =
        world (workspaceRepo "r4d")
        |> withRule (fun r -> r.Route |> Option.exists (fun p -> p.StartsWith "/git/trees/")) (Respond(200L, """{"sha":"x","tree":[],"truncated":true}"""))
        |> init
        |> connect "r4d"
    Assert.Equal("incompatible", text "connState" w)
    Assert.Empty(items "investigations" w)

// ---------------------------------------------------------------------------------------------------------------
// DC-002 / DC-003 regressions (security review F1–F4, F6, F9)
// ---------------------------------------------------------------------------------------------------------------

/// Deliver the submit but answer nothing yet: the write is in flight with its blob requests queued.
let private submitUnanswered (title: string) (w: World) =
    w |> openCommand "new-investigation" |> send "cmd.textA" title |> send "cmd.textB" "in flight" |> deliver (event "cmd.submit" None None)

[<Fact>]
let ``15 sync events during an in-flight write neither replace nor discard it (DC-002)`` () =
    let run = "r15"
    let w0 = connected run (workspaceRepo run)
    let n = w0.Requests.Length
    let inflight = w0 |> submitUnanswered "Racing"
    match sync inflight with
    | InFlight _ -> ()
    | other -> failwithf "expected InFlight, got %A" other
    let poked =
        inflight
        |> deliver (event "sync.reapply" None None)
        |> deliver (event "sync.discard" None None)
        |> deliver (event "sync.retry" None None)
        |> deliver (event "refresh" None None)
    match sync poked with
    | InFlight _ -> ()
    | other -> failwithf "an event replaced the in-flight write: %A" other
    let after = poked |> pump
    Assert.Equal(Idle, sync after)
    Assert.True((live after).LastVerified.IsSome)
    Assert.Equal(1, requestsSince n after |> List.filter isPatch |> List.length)
    patchBodiesNeverForce after

[<Fact>]
let ``15b a second submission while a write is in flight starts nothing (DC-002)`` () =
    let run = "r15b"
    let w0 = connected run (workspaceRepo run)
    let n = w0.Requests.Length
    let first = w0 |> submitUnanswered "First"
    let http (w: World) = w.Pending |> List.filter (function EffectRequest.Http _ -> true | _ -> false) |> List.length
    // A second command composed and submitted before any answer arrives (nothing is pumped in between).
    let second =
        first
        |> deliver (event "cmd.open" None (Some "new-investigation"))
        |> deliver (event "cmd.textA" (Some "Second") None)
        |> deliver (event "cmd.textB" (Some "in flight") None)
        |> deliver (event "cmd.submit" None None)
    Assert.Equal(http first, http second)
    let after = second |> pump
    Assert.Equal(1, requestsSince n after |> List.filter isPatch |> List.length)
    Assert.Equal(Idle, sync after)

[<Fact>]
let ``15c a write answer from an earlier generation is ignored (DC-002)`` () =
    let run = "r15c"
    let w0 = connected run (workspaceRepo run) |> withRule isPostBlob NetworkDown
    let held = w0 |> createInvestigation "Held" "offline first"
    match sync held with
    | Held(_, NotSent _) -> ()
    | other -> failwithf "expected NotSent, got %A" other
    // Forge a late answer carrying an old correlation id: it answers no open question.
    let stale =
        held |> deliver (BrowserToEngineMessage.EffectResult(EffectResult.HttpResult(CorrelationId "gh-1", EffectOutcome.Success(201L, Limen.Contract.RawJson """{"sha":"0000000000000000000000000000000000000000"}""", None))))
    Assert.Equal(sync held, sync stale)
    Assert.Empty(stale.Pending)

[<Fact>]
let ``16 a network failure after the ref update was sent is outcome-unknown, never "not written" (DC-003)`` () =
    let run = "r16"
    let w0 = connected run (workspaceRepo run) |> withRule isPatch NetworkDown
    let after = w0 |> createInvestigation "Lost" "the answer was lost"
    match sync after with
    | Held(_, Unknown _) -> ()
    | other -> failwithf "expected Held Unknown, got %A" other
    Assert.False(flag "canRetry" after)
    Assert.True(flag "canReconcile" after)
    // Reconciling (the PATCH never applied) publishes it as a fast-forward and verifies it.
    let reconciled = after |> fire "sync.reconcile"
    Assert.Equal(Idle, sync reconciled)
    Assert.True((live reconciled).LastVerified.IsSome)
    patchBodiesNeverForce reconciled

[<Fact>]
let ``17 a repository answered under another name is refused, not loaded (F6)`` () =
    let w =
        world (workspaceRepo "r17")
        |> withRule (fun r -> r.Method = "GET" && r.Route = Some "") (Respond(200L, """{"full_name":"someone-else/moved","private":true,"permissions":{"push":true}}"""))
        |> init
        |> connect "r17"
    Assert.Equal("incompatible", text "connState" w)
    Assert.Empty(items "investigations" w)

[<Fact>]
let ``17b a repository answer without a role (installation token) still offers writing and GitHub decides (DC-004)`` () =
    let run = "r17b"
    let w0 =
        world (workspaceRepo run)
        |> withRule (fun r -> r.Method = "GET" && r.Route = Some "") (Respond(200L, sprintf """{"full_name":"%s/%s","private":true}""" owner repoName))
        |> init
        |> connect run
    Assert.True(flag "canCreate" w0)
    let after = w0 |> createInvestigation "Installation" "token without a reported role"
    Assert.Equal(Idle, sync after)
    Assert.True((live after).LastVerified.IsSome)

[<Fact>]
let ``17c a reported read-only role does not gate writing: an installation token reported push false still saves (DC-004)`` () =
    let run = "r17c"
    // What GitHub answered the Actions GITHUB_TOKEN in the live run: a role of push=false, yet the token can write.
    let w0 =
        world (workspaceRepo run)
        |> withRule (fun r -> r.Method = "GET" && r.Route = Some "")
            (Respond(200L, sprintf """{"full_name":"%s/%s","private":true,"permissions":{"admin":false,"push":false,"pull":true}}""" owner repoName))
        |> init
        |> connect run
    Assert.True(flag "canCreate" w0)
    let after = w0 |> createInvestigation "Installation" "role says read-only"
    Assert.Equal(Idle, sync after)
    Assert.True((live after).LastVerified.IsSome)

[<Fact>]
let ``17d when GitHub does refuse the write, the change is held and renewable, never lost`` () =
    let run = "r17d"
    let repo = { workspaceRepo run with CanPush = false }
    let held = connected run repo |> createInvestigation "Refused" "really read-only"
    match sync held with
    | Held(_, WriteDenied _) -> ()
    | other -> failwithf "expected WriteDenied, got %A" other
    Assert.True(flag "canRenew" held)
    Assert.True(flag "canDiscard" held)

[<Fact>]
let ``18 a refused write can be retried with a renewed credential without losing the change (F9)`` () =
    let run = "r18"
    let w0 = connected run (workspaceRepo run) |> withRule isPostBlob (Respond(401L, """{"message":"Bad credentials"}"""))
    let denied = w0 |> createInvestigation "Renew" "token expired"
    match sync denied with
    | Held(_, WriteDenied _) -> ()
    | other -> failwithf "expected WriteDenied, got %A" other
    Assert.True(flag "canRenew" denied)
    Assert.False(flag "canRetry" denied)
    // The owner rotates the credential: GitHub now accepts only the new one, so success proves it was used.
    let rotated = { denied with Repo = { denied.Repo with Token = token + "-renewed" } }
    let renewed = rotated |> send "connect.token" (token + "-renewed") |> fire "auth.renew"
    Assert.Equal(Idle, sync renewed)
    Assert.True((live renewed).LastVerified.IsSome)
    Assert.False((renewed.App.ConnectForm).ContainsKey "token")

// ---------------------------------------------------------------------------------------------------------------
// Routing through Limen.Routing (SAF-URL-9)
// ---------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``19 every Causa route round-trips through the Limen routing codec`` () =
    let id = "a6747bd3-0000-4000-8000-000000000001"
    let entity = "b6747bd3-0000-4000-8000-000000000002"
    let routes =
        [ Home; InvestigationRoute(id, Overview, None) ]
        @ (Tab.all |> List.map (fun t -> InvestigationRoute(id, t, None)) |> List.filter (fun r -> r <> InvestigationRoute(id, Overview, None)))
        @ (Tab.all |> List.map (fun t -> InvestigationRoute(id, t, Some entity)))
    routes |> List.iter (fun r -> Assert.Equal(r, Route.parse (Route.format r)))
    Assert.Equal("#/i/" + id + "/timeline", Route.format (InvestigationRoute(id, Timeline, None)))

[<Fact>]
let ``19b unknown, malformed and non-identifier locations are not found, never another view`` () =
    [ "#/nope"; "#/i/not-a-uuid"; "#/i/a6747bd3-0000-4000-8000-000000000001/bogus"; "#/i/A6747BD3-0000-4000-8000-000000000001"; "#/%zz" ]
    |> List.iter (fun h ->
        match Route.parse h with
        | NotFoundRoute _ -> ()
        | other -> failwithf "%s resolved to %A" h other)
    Assert.Equal(Home, Route.parse "")
    Assert.Equal(Home, Route.parse "#/")

[<Fact>]
let ``19c the committed route inventory is exactly what Limen renders for the table`` () =
    let committed = File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", ".echelon", "routes.json"))
    Assert.Equal(Route.inventory (), committed)

// ---------------------------------------------------------------------------------------------------------------
// Independent closure review regressions (evidence/quality/closure-review-DC-002-004.md, P1–P5)
// ---------------------------------------------------------------------------------------------------------------

/// Answer pending effects in FIFO order, setting aside those matching `late` (returned for delivery later).
let rec private pumpHolding (late: Request -> bool) (held: EffectRequest list) (w: World) : World * EffectRequest list =
    match w.Pending with
    | [] -> w, held
    | (EffectRequest.Http h as e) :: rest when late (requestOf w.Repo h) -> pumpHolding late (held @ [ e ]) { w with Pending = rest }
    | e :: rest -> pumpHolding late held (answer e { w with Pending = rest })

[<Fact>]
let ``20 P1 reconciling an unknown blob creation after the branch moved recreates the blobs instead of throwing`` () =
    let run = "r20"
    let held = connected run (workspaceRepo run) |> withRule isPostBlob (UnknownAfter false) |> createInvestigation "P1" "cancelled blob"
    match sync held with
    | Held(w, Unknown _) -> Assert.True(w.Commit.IsNone)
    | other -> failwithf "expected Held Unknown, got %A" other
    let moved = { held with Repo = held.Repo |> commitFiles "unrelated" [ "README.md", Some(Encoding.UTF8.GetBytes "changed\n") ] }
    let after = moved |> fire "sync.reconcile"
    Assert.Equal(Idle, sync after)
    Assert.True((live after).LastVerified.IsSome)
    patchBodiesNeverForce after

[<Fact>]
let ``20b P2 a re-apply whose target is refused at listing returns to a visible hold`` () =
    let run = "r20b"
    let prefix = Fixtures.workspacePrefix run
    let target = openIds[0]
    let held = connected run (workspaceRepo run) |> withRule isPatch (Concurrent(otherWriterAppends prefix target "other")) |> observe target "mine"
    match sync held with
    | Held(_, Conflict _) -> ()
    | other -> failwithf "expected Held Conflict, got %A" other
    let broken = { held with Repo = held.Repo |> commitFiles "stray" [ prefix + target + "/stray.txt", Some(Encoding.UTF8.GetBytes "x") ] }
    let after = broken |> fire "sync.reapply"
    match sync after with
    | Held(_, Conflict _) -> ()
    | other -> failwithf "expected a visible Held Conflict, got %A" other
    Assert.True((live after).Reapply.IsNone)
    Assert.True(flag "hasHeld" after)
    Assert.True(flag "canDiscard" after)

[<Fact>]
let ``20c P3 a late blob answer during the reload does not start the re-apply from the old state`` () =
    let run = "r20c"
    let prefix = Fixtures.workspacePrefix run
    let target = openIds[0]
    let w0 = connected run (workspaceRepo run)
    // A refresh whose blob reads are held back, then a complete one.
    let stalled, lateBlobs = w0 |> deliver (event "refresh" None None) |> pumpHolding isGetBlob []
    let fresh = stalled |> fire "refresh"
    let held = fresh |> withRule isPatch (Concurrent(otherWriterAppends prefix target "other")) |> observe target "mine"
    match sync held with
    | Held(_, Conflict _) -> ()
    | other -> failwithf "expected Held Conflict, got %A" other
    let patches = held.Requests |> List.filter isPatch |> List.length
    let reloading = held |> deliver (event "sync.reapply" None None)
    let poked = lateBlobs |> List.fold (fun (w: World) e -> answer e w) { reloading with Pending = reloading.Pending }
    match sync poked with
    | InFlight _ -> failwith "a late blob answer started the re-apply before the reload listed the target"
    | _ -> ()
    let after = poked |> pump
    Assert.Equal(Idle, sync after)
    Assert.Equal(patches + 1, after.Requests |> List.filter isPatch |> List.length)
    patchBodiesNeverForce after

[<Fact>]
let ``20d P4 a rate-limited write is retryable, not a credential refusal`` () =
    let run = "r20d"
    let w0 = connected run (workspaceRepo run) |> withRule isPostBlob (Respond(403L, """{"message":"API rate limit exceeded for installation."}"""))
    let held = w0 |> createInvestigation "Rate" "limited"
    match sync held with
    | Held(_, WriteFailed _) -> ()
    | other -> failwithf "expected a retryable WriteFailed, got %A" other
    Assert.True(flag "canRetry" held)
    let retried = held |> fire "sync.retry"
    Assert.Equal(Idle, sync retried)

[<Fact>]
let ``20e P5 a late error from the held attempt does not abort the reconciliation`` () =
    let run = "r20e"
    let w0 = connected run (workspaceRepo run)
    let submitted = w0 |> openCommand "new-investigation" |> send "cmd.textA" "P5" |> send "cmd.textB" "late" |> deliver (event "cmd.submit" None None)
    let verifying, late = submitted |> pumpHolding (fun r -> isGetBlob r && r.CorrelationId <> "") []
    Assert.Equal(3, late.Length)
    let failed = { verifying with Rules = [] } |> withRule isGetBlob NetworkDown |> answer late[0]
    match sync failed with
    | Held(_, Unknown _) -> ()
    | other -> failwithf "expected Held Unknown, got %A" other
    let reconciling = { failed with Rules = [] } |> deliver (event "sync.reconcile" None None)
    let poked = reconciling |> withRule isGetBlob NetworkDown |> answer late[1]
    match sync poked with
    | InFlight(_, Reconciling) -> ()
    | other -> failwithf "a late answer from the held attempt changed the reconciliation: %A" other
    let after = { poked with Rules = [] } |> pump
    Assert.Equal(Idle, sync after)

[<Fact>]
let ``21 reads of mutable state are never cacheable: every ref and repository read has a unique URL (DC-005)`` () =
    let run = "r21"
    let after = connected run (workspaceRepo run) |> createInvestigation "Fresh" "no cached ref" |> fire "refresh"
    let isRefOrRepo (r: Request) = r.Method = "GET" && (r.Route = Some "" || r.Route |> Option.exists (fun p -> p.StartsWith "/git/ref/"))
    let fresh = after.Requests |> List.filter isRefOrRepo |> List.map (fun r -> r.Url)
    Assert.True(fresh.Length >= 4)
    Assert.All(fresh, fun u -> Assert.Contains("fresh=", u))
    Assert.Equal(fresh.Length, (List.distinct fresh).Length)
    // A fresh engine (a page reload) restarts its correlation ids; its fresh URLs must still differ from the first session's.
    // (Production seeds are random per message; the harness models the new page with a different seed range.)
    let reloaded = { world after.Repo with Step = 4_000 } |> init |> connect run
    let again = reloaded.Requests |> List.filter isRefOrRepo |> List.map (fun r -> r.Url)
    Assert.Empty(Set.intersect (Set.ofList fresh) (Set.ofList again))
    // Content-addressed reads keep their stable URL: caching them is correct.
    let addressed = after.Requests |> List.filter (fun r -> r.Method = "GET" && not (isRefOrRepo r)) |> List.map (fun r -> r.Url)
    Assert.All(addressed, fun u -> Assert.DoesNotContain("fresh=", u))

// ---------------------------------------------------------------------------------------------------------------
// Second independent closure review (evidence/quality/closure-review-2.md, A, C, D)
// ---------------------------------------------------------------------------------------------------------------

let private isGetRef (r: Request) = r.Method = "GET" && (r.Route |> Option.exists (fun p -> p.StartsWith "/git/ref/"))
let private isGetCommit (r: Request) = r.Method = "GET" && (r.Route |> Option.exists (fun p -> p.StartsWith "/git/commits/"))
let private isPostCommit (r: Request) = r.Method = "POST" && r.Route = Some "/git/commits"
let private patchedShas (w: World) =
    w.Requests |> List.filter isPatch |> List.choose (fun r -> r.Body) |> List.map (fun b -> Text.Json.JsonDocument.Parse(b).RootElement.GetProperty("sha").GetString())

[<Fact>]
let ``22 A no write starts while a reload runs, so a late reload answer cannot roll back a verified save`` () =
    let run = "r22"
    let denied = connected run (workspaceRepo run) |> withRule isPostBlob (Respond(401L, """{"message":"Bad credentials"}""")) |> createInvestigation "Renew" "during refresh"
    // A reload starts; its commit read is held back.
    let reloading, late = { denied with Rules = [] } |> deliver (event "refresh" None None) |> pumpHolding isGetCommit []
    Assert.True((live reloading).Refreshing)
    Assert.False(flag "canRenew" reloading)
    let refused = reloading |> send "connect.token" token |> fire "auth.renew"
    match sync refused with
    | Held(_, WriteDenied _) -> ()
    | other -> failwithf "a write started during a reload: %A" other
    Assert.Equal(0, requestsSince reloading.Requests.Length refused |> List.filter isWrite |> List.length)
    // The reload finishes; then the renewed save is verified and the view shows it.
    let reloaded = late |> List.fold (fun (w: World) e -> answer e w) refused |> pump
    Assert.False((live reloaded).Refreshing)
    let saved = reloaded |> send "connect.token" token |> fire "auth.renew"
    Assert.Equal(Idle, sync saved)
    Assert.Equal(saved.Repo.Head, (live saved).Head.Commit)

[<Fact>]
let ``22b C after an accepted PATCH, a branch diverged elsewhere is outcome-unknown; a stale ref read is not`` () =
    let run = "r22b"
    let w0 = connected run (workspaceRepo run)
    let before = w0.Repo.Head
    // Right after our PATCH someone force-moves the branch to another line of history (parent: the old head).
    let diverge (repo: Repo) = commitFiles "someone else's line" [ "README.md", Some(Encoding.UTF8.GetBytes "other\n") ] { repo with Head = before }
    let forced =
        w0
        |> withRule isPatch (Concurrent id)
        |> withRule isGetRef (Concurrent diverge)
        |> createInvestigation "Forced" "diverged after patch"
    match sync forced with
    | Held(_, Unknown _) -> ()
    | other -> failwithf "a diverged branch after the PATCH was reported as %A" other
    Assert.True((live forced).LastVerified.IsNone)
    // A stale replica: the verify-ref read answers the previous head although the branch holds our commit. GitHub
    // accepted the non-forced PATCH, so the commit read-back decides. (A force-reset to exactly the previous head is
    // indistinguishable from this by any single read; recorded as a residual in DC-005.)
    let runS = run + "s"
    let fresh = connected runS (workspaceRepo runS)
    let stale =
        fresh
        |> withRule isPatch (Concurrent id)
        |> withRule isGetRef (Respond(200L, sprintf """{"ref":"refs/heads/main","object":{"sha":"%s","type":"commit"}}""" fresh.Repo.Head))
        |> createInvestigation "Stale" "replica lag"
    Assert.Equal(Idle, sync stale)
    Assert.True((live stale).LastVerified.IsSome)

[<Fact>]
let ``22c D a rebased publication never re-sends its pre-rebase commit`` () =
    let run = "r22c"
    let prefix = Fixtures.workspacePrefix run
    // The first PATCH meets another writer; after the rebase, the new commit creation's answer is lost.
    let lost =
        connected run (workspaceRepo run)
        |> withRule isPatch (Concurrent(otherWriterAppends prefix openIds[1] "other"))
        |> withRule isPostCommit (Concurrent id)
        |> withRule isPostCommit (UnknownAfter false)
        |> createInvestigation "D" "lost commit answer"
    match sync lost with
    | Held(_, Unknown _) -> ()
    | other -> failwithf "expected Held Unknown, got %A" other
    let after = { lost with Rules = [] } |> fire "sync.reconcile"
    Assert.Equal(Idle, sync after)
    let shas = patchedShas after
    Assert.Equal(shas.Length, (List.distinct shas).Length)
    patchBodiesNeverForce after

// ---------------------------------------------------------------------------------------------------------------
// Third independent closure review (evidence/quality/closure-review-3.md)
// ---------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``23 a reload answered by a lagging replica never moves the view back past a verified save (DC-005)`` () =
    let run = "r23"
    let w0 = connected run (workspaceRepo run)
    let oldHead = w0.Repo.Head
    let saved = w0 |> createInvestigation "Saved" "then a stale reload"
    let count = (live saved).Catalog.Length
    let newHead = (live saved).Head.Commit
    Assert.Equal(saved.Repo.Head, newHead)
    let reloaded =
        saved
        |> withRule isGetRef (Respond(200L, sprintf """{"ref":"refs/heads/main","object":{"sha":"%s","type":"commit"}}""" oldHead))
        |> fire "refresh"
    Assert.Equal(newHead, (live reloaded).Head.Commit)
    Assert.Equal(count, (live reloaded).Catalog.Length)
    Assert.False((live reloaded).Refreshing)
    // A genuinely newer head is still adopted.
    let prefix = Fixtures.workspacePrefix run
    let moved = { reloaded with Rules = []; Repo = reloaded.Repo |> otherWriterAppends prefix openIds[0] "someone else" }
    let fresh = moved |> fire "refresh"
    Assert.Equal(moved.Repo.Head, (live fresh).Head.Commit)

[<Fact>]
let ``23b a renewal refused during a reload changes nothing and keeps the typed token in the form`` () =
    let run = "r23b"
    let denied = connected run (workspaceRepo run) |> withRule isPostBlob (Respond(401L, """{"message":"Bad credentials"}""")) |> createInvestigation "Renew" "during refresh"
    let reloading, _ = { denied with Rules = [] } |> deliver (event "refresh" None None) |> pumpHolding isGetCommit []
    let before = (live reloading).Token
    let refused = reloading |> send "connect.token" (token + "-new") |> fire "auth.renew"
    Assert.Equal(before, (live refused).Token)
    Assert.Equal(Some(token + "-new"), refused.App.ConnectForm |> Map.tryFind "token")

// ---------------------------------------------------------------------------------------------------------------
// Fourth independent closure review (evidence/quality/closure-review-4.md, Q1-Q4)
// ---------------------------------------------------------------------------------------------------------------

let private refBody (sha: string) = sprintf """{"ref":"refs/heads/main","object":{"sha":"%s","type":"commit"}}""" sha
let private isCompare (r: Request) = r.Method = "GET" && (r.Route |> Option.exists (fun p -> p.StartsWith "/compare/"))

[<Fact>]
let ``24 Q1 the reload after a moved save is anchored on the verified save, not the old read point`` () =
    let run = "r24"
    let prefix = Fixtures.workspacePrefix run
    let w0 = connected run (workspaceRepo run)
    let h0 = w0.Repo.Head
    let after =
        w0
        |> withRule isGetRef (Concurrent(otherWriterAppends prefix openIds[1] "after patch"))
        |> withRule isGetRef (Respond(200L, refBody h0))
        |> createInvestigation "Auto" "moved save"
    Assert.Equal(after.Repo.Head, (live after).Head.Commit)
    Assert.Equal(6, (live after).Catalog.Length)

[<Fact>]
let ``24b Q2b the reload after a rebased save never adopts the rebase's intermediate head`` () =
    let run = "r24b"
    let prefix = Fixtures.workspacePrefix run
    let mid = ref ""
    let seen = ref 0
    // The first PATCH meets another writer's commit (mid); the engine rebases and saves on top of it.
    let submitted =
        connected run (workspaceRepo run)
        |> withRule isPatch (Concurrent(fun repo -> let moved = otherWriterAppends prefix openIds[1] "other" repo in mid.Value <- moved.Head; moved))
        |> openCommand "new-investigation" |> send "cmd.textA" "Rebased" |> send "cmd.textB" "auto reload"
        |> deliver (event "cmd.submit" None None)
    // Hold back the third ref read (rebase reload, verify, then the automatic reload) and answer it with mid.
    let paused, held = submitted |> pumpHolding (fun r -> isGetRef r && (seen.Value <- seen.Value + 1; seen.Value = 3)) []
    Assert.Equal(1, held.Length)
    let after = { paused with Rules = [] } |> withRule isGetRef (Respond(200L, refBody mid.Value)) |> answer held[0] |> pump
    Assert.Equal(after.Repo.Head, (live after).Head.Commit)
    Assert.Equal(6, (live after).Catalog.Length)

[<Fact>]
let ``24c Q3 a deliberate rollback is shown after one confirming read, and the next save builds on it`` () =
    let run = "r24c"
    let w0 = connected run (workspaceRepo run)
    let h0 = w0.Repo.Head
    let saved = w0 |> createInvestigation "First" "then rolled back"
    let c1 = saved.Repo.Head
    let rolled = { saved with Repo = { saved.Repo with Head = h0 } } |> fire "refresh"
    Assert.Equal(h0, (live rolled).Head.Commit)
    Assert.Equal(5, (live rolled).Catalog.Length)
    let second = rolled |> createInvestigation "Second" "after rollback"
    Assert.Equal<string list>([ h0 ], (headCommit second.Repo).Parents)
    Assert.True(c1 <> (headCommit second.Repo).Parents.Head)

[<Fact>]
let ``24d Q4 a compare GitHub cannot answer adopts the ref instead of failing every reload`` () =
    let run = "r24d"
    let prefix = Fixtures.workspacePrefix run
    let w0 = connected run (workspaceRepo run)
    let moved = { w0 with Repo = w0.Repo |> otherWriterAppends prefix openIds[0] "someone" }
    let after =
        moved
        |> withRule isCompare (Respond(404L, """{"message":"No common ancestor between a and b."}"""))
        |> fire "refresh"
    Assert.Equal(moved.Repo.Head, (live after).Head.Commit)
    Assert.False((live after).Refreshing)
