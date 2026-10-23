namespace Causa.Engine

open System
open Limen.Routing

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

/// Causa's URL space as a Limen routing table (SAF-URL-9): hash mode for GitHub Pages, identifiers only, never a
/// credential or investigation content (Limen refuses credential-like parameter names when the table is defined).
[<RequireQualifiedAccess>]
module Route =
    let private uuid = Text.RegularExpressions.Regex(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z")
    let private tabs = "{tab:enum:" + (Tab.all |> List.map Tab.key |> String.concat "|") + "}"

    let table =
        match
            RouteTable.define
                [ Limen.Routing.Route.create "home" ""
                  Limen.Routing.Route.create "investigation" "i/{id}"
                  Limen.Routing.Route.create "tab" ("i/{id}/" + tabs)
                  Limen.Routing.Route.create "entity" ("i/{id}/" + tabs + "/{entity}")
                  Limen.Routing.Route.create "notFound" "{*rest}" ]
                []
                { Home = "home"; SignIn = None; NotFound = Some "notFound" }
        with
        | Ok t -> t
        | Error problems -> failwithf "Causa route table is invalid: %A" problems

    let private text (key: string) (m: Map<string, Value>) = match m.TryFind key with Some(Value.Text v) -> Some v | _ -> None

    let private toTarget (route: Route) : Target =
        // "#/i/<id>" is accepted on parse; the canonical form always names the tab.
        let target name parameters = { Route = name; Params = Map.ofList [ for k, v in parameters -> k, Value.Text v ]; Query = Map.empty }
        match route with
        | Home -> target "home" []
        | InvestigationRoute(id, tab, None) -> target "tab" [ "id", id; "tab", Tab.key tab ]
        | InvestigationRoute(id, tab, Some e) -> target "entity" [ "id", id; "tab", Tab.key tab; "entity", e ]
        | NotFoundRoute p -> target "notFound" [ "rest", p.TrimStart '/' ]

    let private ofMatch (m: Match) : Result<Route, string> =
        let p = m.Chain |> List.last |> fun l -> l.Params
        let id = text "id" p |> Option.filter uuid.IsMatch
        let tab = text "tab" p |> Option.bind Tab.tryParse
        let entity = text "entity" p |> Option.filter uuid.IsMatch
        match m.Route, id, tab, entity with
        | "home", _, _, _ -> Ok Home
        | "investigation", Some i, _, _ -> Ok(InvestigationRoute(i, Overview, None))
        | "tab", Some i, Some t, _ -> Ok(InvestigationRoute(i, t, None))
        | "entity", Some i, Some t, Some e -> Ok(InvestigationRoute(i, t, Some e))
        | route, _, _, _ -> Error("not a Causa identifier in " + route)

    let codec = RouteCodec.create table toTarget ofMatch

    let parse (hash: string) : Route =
        let location = Location.ofBrowser LocationMode.Hash { Origin = ""; Path = "/"; Query = ""; Hash = hash }
        match RouteCodec.parse codec (fun _ _ -> GuardDecision.Allow) location with
        | Ok route -> route
        | Error _ -> NotFoundRoute(location.Split('?')[0])

    let format (route: Route) : string =
        match RouteCodec.format codec route with
        | Ok location -> Location.href LocationMode.Hash location
        | Error _ -> "#/"

    /// The `echelon.routes/v1` inventory, byte-identical to what Limen renders for this table.
    let inventory () = Inventory.render LocationMode.Hash table
