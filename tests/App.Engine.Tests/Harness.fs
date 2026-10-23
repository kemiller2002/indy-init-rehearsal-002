/// A deterministic driver: the engine (Shell.handle) on one side, the fake GitHub on the other, every message
/// supplied with a fixed seed and instant. The world is an immutable record; each step returns a new one.
module App.Engine.Tests.Harness

open System
open Limen.Contract
open Limen.Contract.Core
open Causa.Engine
open App.Engine.Tests.FakeGitHub

/// What the fake does instead of (or before) answering normally. Each rule fires once.
type Action =
    | Respond of status: int64 * body: string
    | NetworkDown
    | UnknownAfter of applied: bool
    | Concurrent of (Repo -> Repo)

type Rule =
    { Matches: Request -> bool
      Action: Action }

type World =
    { App: App
      Repo: Repo
      Rules: Rule list
      /// Every Http request the engine emitted that was answered, in order.
      Requests: Request list
      /// Every engine response, in order.
      Responses: EngineToBrowserMessage list
      Pending: EffectRequest list
      Step: int }

let token = "ghs_TESTcredential0123456789abcdefXYZ"
let baseTime = DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero)
let seedOf (n: int) = Guid(sprintf "%08x-0000-4000-8000-00000000c0de" n)
let nowOf (n: int) = baseTime.AddSeconds(float n)

let offer : HostHandshake =
    { Protocol = { Major = Contract.ProtocolVersion; Minor = Contract.ProtocolMinor }
      Contract = { Unit = Contract.Unit; Version = Contract.Version; Fingerprint = Contract.Fingerprint }
      Capabilities = [] }

let location (hash: string) : BrowserLocation = { Origin = "https://example.test"; Path = "/"; Query = ""; Hash = hash }

let world (repo: Repo) =
    { App = App.initial; Repo = repo; Rules = []; Requests = []; Responses = []; Pending = []; Step = 0 }

let withRule (matches: Request -> bool) (action: Action) (w: World) = { w with Rules = w.Rules @ [ { Matches = matches; Action = action } ] }

/// Deliver one message to the engine; resulting effects are queued, not answered.
let deliver (message: BrowserToEngineMessage) (w: World) : World =
    let app = Shell.withContext (seedOf w.Step) (nowOf w.Step) w.App
    let next, response = Shell.handle app message
    { w with App = next; Responses = w.Responses @ [ response ]; Pending = w.Pending @ response.Effects; Step = w.Step + 1 }

let requestOf (repo: Repo) (h: HttpEffectRequest) : Request =
    let (CorrelationId cid) = h.CorrelationId
    { CorrelationId = cid
      Method =
        match h.Method with
        | HttpMethod.Get -> "GET"
        | HttpMethod.Post -> "POST"
        | HttpMethod.Patch -> "PATCH"
        | HttpMethod.Put -> "PUT"
        | HttpMethod.Delete -> "DELETE"
        | HttpMethod.Head -> "HEAD"
        | HttpMethod.Options -> "OPTIONS"
      Url = h.Url
      Route = routeOf repo h.Url
      Body = h.Body
      Authorization = h.Headers |> Option.bind (Map.tryFind "Authorization") }

let private removeFirst (rule: Rule) (rules: Rule list) =
    let i = rules |> List.findIndex (fun r -> obj.ReferenceEquals(r, rule))
    rules |> List.removeAt i

/// Answer one queued effect.
let answer (effect: EffectRequest) (w: World) : World =
    match effect with
    | EffectRequest.Http h ->
        let req = requestOf w.Repo h
        let rule = w.Rules |> List.tryFind (fun r -> r.Matches req)
        let rules = match rule with Some r -> removeFirst r w.Rules | None -> w.Rules
        let normal repo = let s, b, r = handle req repo in EffectOutcome.Success(s, RawJson b, None), r
        let outcome, repo =
            match rule |> Option.map (fun r -> r.Action) with
            | None -> normal w.Repo
            | Some(Respond(s, b)) -> EffectOutcome.Success(s, RawJson b, None), w.Repo
            | Some NetworkDown -> EffectOutcome.Failure(HttpFailureReason.Network, None), w.Repo
            | Some(UnknownAfter applied) ->
                let _, r = normal w.Repo
                EffectOutcome.OutcomeUnknown OutcomeUnknownReason.TimeoutAfterDispatch, (if applied then r else w.Repo)
            | Some(Concurrent f) -> normal (f w.Repo)
        { w with Repo = repo; Rules = rules; Requests = w.Requests @ [ req ] }
        |> deliver (BrowserToEngineMessage.EffectResult(EffectResult.HttpResult(h.CorrelationId, outcome)))
    | EffectRequest.Navigation(NavigationEffectRequest.Push(cid, url)) ->
        let hash = match url.IndexOf '#' with -1 -> "" | i -> url.Substring i
        w |> deliver (BrowserToEngineMessage.EffectResult(EffectResult.NavigationResult(cid, NavigationOutcome.Success(location hash))))
    | _ -> w

/// Answer queued effects (FIFO) until none remain.
let rec pump (w: World) : World =
    if w.Step > 5000 then failwith "engine did not settle"
    match w.Pending with
    | [] -> w
    | e :: rest -> pump (answer e { w with Pending = rest })

let event (name: string) (value: string option) (key: string option) : BrowserToEngineMessage =
    BrowserToEngineMessage.Event { Name = name; Key = key; Value = value; Checked = None; Values = None; Submitter = None }

let send name value w = w |> deliver (event name (Some value) None) |> pump
let fire name w = w |> deliver (event name None None) |> pump
let openCommand key w = w |> deliver (event "cmd.open" None (Some key)) |> pump
let goTo (hash: string) w = w |> deliver (BrowserToEngineMessage.LocationChanged(location hash)) |> pump

let init (w: World) = w |> deliver (BrowserToEngineMessage.Initialize([], location "#/", Some offer))

let fillConnect (owner: string) (repo: string) (branch: string) (root: string) (credential: string) (w: World) =
    w
    |> send "connect.owner" owner
    |> send "connect.repo" repo
    |> send "connect.branch" branch
    |> send "connect.root" root
    |> send "connect.token" credential

let connectAs owner repo branch root credential w = w |> fillConnect owner repo branch root credential |> fire "connect.submit"
let connect (root: string) (w: World) = w |> connectAs w.Repo.Owner w.Repo.Name w.Repo.Branch root token

// ---------------------------------------------------------------------------------------------------------------
// Observations
// ---------------------------------------------------------------------------------------------------------------

let lastResponse (w: World) = List.last w.Responses
let view (w: World) : ViewState = (lastResponse w).View

let text (key: string) (w: World) =
    match view w |> Map.tryFind key with
    | Some(ViewValue.Text s) -> s
    | other -> failwithf "view key %s is not text: %A" key other

let flag (key: string) (w: World) =
    match view w |> Map.tryFind key with
    | Some(ViewValue.Flag b) -> b
    | other -> failwithf "view key %s is not a flag: %A" key other

let number (key: string) (w: World) =
    match view w |> Map.tryFind key with
    | Some(ViewValue.Number n) -> n
    | other -> failwithf "view key %s is not a number: %A" key other

let items (key: string) (w: World) =
    match view w |> Map.tryFind key with
    | Some(ViewValue.Items xs) -> xs
    | other -> failwithf "view key %s is not a list: %A" key other

let itemText (field: string) (item: Map<string, ViewPrimitive>) =
    match item |> Map.tryFind field with
    | Some(ViewPrimitive.Text s) -> s
    | other -> failwithf "item field %s is not text: %A" field other

let live (w: World) =
    match w.App.Connection with
    | Connected l -> l
    | other -> failwithf "not connected: %A" other

let entry (id: string) (w: World) = (live w).Catalog |> List.find (fun e -> e.Id = id)

let httpEffects (response: EngineToBrowserMessage) =
    response.Effects |> List.choose (function EffectRequest.Http h -> Some h | _ -> None)

let isWrite (r: Request) = r.Method <> "GET"
let requestsSince (n: int) (w: World) = w.Requests |> List.skip n

/// Short form of a request for sequence assertions: "POST /git/blobs", "GET /git/ref" ...
let shape (r: Request) =
    let route = r.Route |> Option.defaultValue r.Url
    let kind =
        if r.Url.EndsWith "/graphql" then "graphql"
        elif route = "" then "repo"
        elif route.StartsWith "/git/ref/" || route.StartsWith "/git/refs/" then "ref"
        elif route.StartsWith "/git/commits" then "commit"
        elif route.StartsWith "/git/trees" then "tree"
        elif route.StartsWith "/git/blobs" then "blob"
        else route
    r.Method + " " + kind

/// The one publication request: GraphQL createCommitOnBranch (guarded by expectedHeadOid).
let isSave (r: Request) = r.Method = "POST" && r.Url.EndsWith "/graphql"
// The REST write sequence (blobs, tree, commit, PATCH) is gone; earlier tests name the publication by these.
let isPatch = isSave
let isPostBlob = isSave
let isGetBlob (r: Request) = r.Method = "GET" && (r.Route |> Option.exists (fun p -> p.StartsWith "/git/blobs/"))
