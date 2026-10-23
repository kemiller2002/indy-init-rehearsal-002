namespace Causa.Engine

open System
open IndyInitRootCauseInvestigator.Engine

type Tab =
    | Overview
    | Hypotheses
    | EvidenceTab
    | Questions
    | Causes
    | Timeline
    | Report

/// Hash routes (GitHub Pages never sees them, so a reloaded deep link cannot 404). The URL never carries a
/// credential or investigation content (R9.9): only investigation and entity identifiers.
type Route =
    | Home
    | InvestigationRoute of id: string * tab: Tab * entity: string option
    | NotFoundRoute of path: string

type Dialog =
    { Kind: CommandKind
      Target: Guid option
      Draft: FormDraft
      Error: string option
      Confirming: bool }

type Tone =
    | Neutral
    | Positive
    | Attention
    | Critical

type Notice = { Tone: Tone; Text: string }

type BootState =
    | Starting
    | Running
    | HandshakeRefused of string

type App =
    { Boot: BootState
      Base: string
      Route: Route
      Connection: Connection
      ConnectForm: Map<string, string>
      Dialog: Dialog option
      Notice: Notice option
      Inflight: Map<string, Op>
      Counter: int
      Context: Context
      TimelineFilter: string
      SortBy: string }

[<RequireQualifiedAccess>]
module Tab =
    let all = [ Overview; Hypotheses; EvidenceTab; Questions; Causes; Timeline; Report ]
    let key =
        function
        | Overview -> "overview" | Hypotheses -> "hypotheses" | EvidenceTab -> "evidence" | Questions -> "questions"
        | Causes -> "causes" | Timeline -> "timeline" | Report -> "report"
    let label =
        function
        | Overview -> "Overview" | Hypotheses -> "Hypotheses" | EvidenceTab -> "Evidence" | Questions -> "Questions & conflicts"
        | Causes -> "Causes & verification" | Timeline -> "Timeline" | Report -> "Report"
    let tryParse (s: string) = all |> List.tryFind (fun t -> key t = s)

[<RequireQualifiedAccess>]
module Route =
    let private uuid = Text.RegularExpressions.Regex(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z")

    let parse (hash: string) : Route =
        let path = (if hash.StartsWith("#", StringComparison.Ordinal) then hash.Substring 1 else hash).Split('?')[0]
        match path.Split('/', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray with
        | [] -> Home
        | [ "i"; id ] when uuid.IsMatch id -> InvestigationRoute(id, Overview, None)
        | [ "i"; id; tab ] when uuid.IsMatch id -> (match Tab.tryParse tab with Some t -> InvestigationRoute(id, t, None) | None -> NotFoundRoute path)
        | [ "i"; id; tab; entity ] when uuid.IsMatch id && uuid.IsMatch entity ->
            (match Tab.tryParse tab with Some t -> InvestigationRoute(id, t, Some entity) | None -> NotFoundRoute path)
        | _ -> NotFoundRoute path

    let format (route: Route) : string =
        match route with
        | Home -> "#/"
        | InvestigationRoute(id, tab, None) -> "#/i/" + id + "/" + Tab.key tab
        | InvestigationRoute(id, tab, Some e) -> "#/i/" + id + "/" + Tab.key tab + "/" + e
        | NotFoundRoute p -> "#" + p

[<RequireQualifiedAccess>]
module App =
    let initial =
        { Boot = Starting
          Base = "/"
          Route = Home
          Connection = Disconnected
          ConnectForm = Map.ofList [ "owner", ""; "repo", ""; "branch", ""; "root", "" ]
          Dialog = None
          Notice = None
          Inflight = Map.empty
          Counter = 0
          Context = Context.initial
          TimelineFilter = "all"
          SortBy = "updated" }

    let live (app: App) = match app.Connection with Connected l -> Some l | _ -> None

    let withLive (f: Live -> Live) (app: App) =
        match app.Connection with
        | Connected l -> { app with Connection = Connected(f l) }
        | _ -> app

    let notice tone text (app: App) = { app with Notice = Some { Tone = tone; Text = text } }
