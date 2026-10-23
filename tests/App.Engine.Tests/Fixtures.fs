/// Test-only access to the frozen causa-data v1.0.0 checkout (.cache/causa-data, tag causa-storage-v1.0.0).
/// The data is read at test time and handed to the in-memory fake GitHub; it never enters src/.
module App.Engine.Tests.Fixtures

open System
open System.IO
open Causa.Contract
open Causa.Engine

/// Missing data is a failure, never a skip.
let dataRoot =
    let fallback = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", ".cache", "causa-data"))
    let candidate =
        match Environment.GetEnvironmentVariable "CAUSA_DATA_DIR" with
        | null -> fallback
        | s when String.IsNullOrWhiteSpace s -> fallback
        | s -> s
    if not (File.Exists(Path.Combine(candidate, "fixtures", "v1.0.0", "manifest.json"))) then
        failwithf "causa-data fixtures not found at %s; run tools/data/fetch-causa-data.sh" candidate
    candidate

let private investigationsDir = Path.Combine(dataRoot, "fixtures", "v1.0.0", "investigations")

/// The five frozen investigation directory names (lowercase UUIDs), sorted.
let ids : string list =
    Directory.GetDirectories investigationsDir
    |> Array.map (fun d -> Path.GetFileName d |> nonNull)
    |> Array.sort
    |> Array.toList

/// file name -> bytes for one frozen investigation.
let files (id: string) : (string * byte[]) list =
    Store.fileNames |> List.map (fun name -> name, File.ReadAllBytes(Path.Combine(investigationsDir, id, name)))

let fixturesPrefix = "fixtures/v1.0.0/investigations/"
let workspacePrefix (run: string) = "workspaces/demo/" + run + "/investigations/"

/// Repository paths and bytes for the five investigations placed under a prefix.
let under (prefix: string) : (string * byte[]) list =
    ids |> List.collect (fun id -> files id |> List.map (fun (name, bytes) -> prefix + id + "/" + name, bytes))

let schemaSet = Schemas.set.Force() |> Result.defaultWith (fun e -> failwithf "%s %s" e.Code e.Detail)

let loaded (id: string) : InvestigationFiles * Loaded =
    let fileMap = files id |> Map.ofList
    let f = Store.ofFileMap id fileMap |> Result.defaultWith (fun e -> failwithf "%s %s" e.Code e.Detail)
    f, Store.validate schemaSet f |> Result.defaultWith (fun e -> failwithf "%s %s" e.Code e.Detail)

let fileMapOf (f: InvestigationFiles) : (string * byte[]) list =
    [ "manifest.json", f.Manifest; "events.ndjson", f.Events; "snapshot.json", f.Snapshot ]
