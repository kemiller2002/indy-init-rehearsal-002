namespace Causa.Engine

open System
open IndyInitRootCauseInvestigator.Engine

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
      /// Correlation id → the question asked and the write generation that asked it (0 = not a write).
      Inflight: Map<string, Op * int>
      Counter: int
      Context: Context
      TimelineFilter: string
      SortBy: string }

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
