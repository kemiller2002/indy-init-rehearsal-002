module Causa.Conformance.Tests

open System
open System.IO
open Xunit
open Causa.Contract
open Causa.Engine

/// The test-only causa-data checkout (tools/data/fetch-causa-data.sh). Missing data is a failure, never a skip.
let private dataRoot =
    let candidate =
        match Environment.GetEnvironmentVariable "CAUSA_DATA_DIR" with
        | null -> Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", ".cache", "causa-data"))
        | fromEnv when String.IsNullOrWhiteSpace fromEnv -> Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", ".cache", "causa-data"))
        | fromEnv -> fromEnv
    if not (File.Exists(Path.Combine(candidate, "fixtures", "v1.0.0", "manifest.json"))) then
        failwithf "causa-data fixtures not found at %s; run tools/data/fetch-causa-data.sh" candidate
    candidate

let private fixtures = Path.Combine(dataRoot, "fixtures", "v1.0.0")
let private schemaSet = Schemas.set.Force() |> Result.defaultWith (fun e -> failwithf "%s %s" e.Code e.Detail)
let private parseFile (path: string) =
    File.ReadAllBytes path |> Json.decodeUtf8 |> Result.bind (fun t -> Json.parse (t.TrimEnd '\n')) |> Result.defaultWith failwith
let private rootManifest = parseFile (Path.Combine(fixtures, "manifest.json"))
let private field (name: string) (v: JValue) =
    match v with
    | JObj o -> Json.field name o
    | _ -> None
let private str name v = match field name v with Some(JStr s) -> s | _ -> ""
let private arr name v = match field name v with Some(JArr xs) -> xs | _ -> []

let private entries = arr "investigations" rootManifest
let private loadScenario (entry: JValue) =
    let id = str "investigationId" entry
    let files = Store.readDirectory (Path.Combine(fixtures, "investigations", id)) |> Result.defaultWith (fun e -> failwithf "%s %s" e.Code e.Detail)
    str "scenarioKey" entry, files, Store.validate schemaSet files

[<Fact>]
let ``the embedded schema bundle is the contract's 60 schemas`` () =
    Assert.Equal(60, Schemas.count ())

[<Fact>]
let ``the frozen dataset lists five scenarios`` () =
    Assert.Equal(5, List.length entries)

[<Fact>]
let ``every golden scenario validates, replays through the domain and matches its frozen state digest`` () =
    for entry in entries do
        let key, _, result = loadScenario entry
        match result with
        | Error e -> failwithf "%s rejected: %s %s" key e.Code e.Detail
        | Ok loaded ->
            Assert.Equal(str "expectedStateSha256" entry, loaded.StateSha256)
            Assert.Equal(str "eventsSha256" entry, loaded.EventsSha256)
            Assert.Equal(str "snapshotSha256" entry, loaded.SnapshotSha256)
            let golden = parseFile (Path.Combine(dataRoot, "contract", "v1.0.0", "golden", key + ".state.json"))
            Assert.Equal(Json.canonical golden, Json.canonical loaded.StateJson)

let private byKey =
    lazy (entries |> List.map loadScenario |> List.map (fun (k, f, r) -> k, (f, Result.defaultWith (fun (e: CausaError) -> failwith e.Code) r)) |> Map.ofList)

let private fromBase64 (v: JValue option) =
    match v with
    | Some(JObj o) -> (match Json.field "base64" o with Some(JStr s) -> Convert.FromBase64String s | _ -> [||])
    | _ -> [||]

let private filesFrom (directoryId: string) (v: JValue option) =
    match v with
    | Some(JObj o) -> o |> List.map (fun (k, x) -> k, fromBase64 (Some x)) |> Map.ofList |> Store.ofFileMap directoryId
    | _ -> CausaError.fail Codes.schemaViolation "files must be an object"

/// The reader's verdict on one negative fixture (Ok = wrongly accepted).
let private evaluate (fixture: JValue) : Result<unit, CausaError> =
    match str "kind" fixture with
    | "investigation" -> filesFrom (str "directoryId" fixture) (field "files" fixture) |> Result.bind (Store.validate schemaSet) |> Result.map ignore
    | "append" ->
        let _, loaded = byKey.Force() |> Map.find (str "baseScenarioKey" fixture)
        let expected = match field "expectedAggregateVersion" fixture with Some(JInt n) -> n | _ -> -1L
        Store.appendLines schemaSet loaded expected (fromBase64 (field "appendedEvents" fixture)) |> Result.map ignore
    | "successor" ->
        let files, loaded = byKey.Force() |> Map.find (str "baseScenarioKey" fixture)
        filesFrom files.DirectoryId (field "files" fixture) |> Result.bind (Store.successor schemaSet loaded files.Events) |> Result.map ignore
    | other -> CausaError.fail Codes.schemaViolation ("unknown negative kind " + other)

let negatives : TheoryData<string, string> =
    let data = TheoryData<string, string>()
    arr "negativeFixtures" rootManifest |> List.iter (fun n -> data.Add(str "path" n, str "expectedErrorCode" n))
    data

[<Fact>]
let ``the frozen dataset lists ninety negative fixtures`` () =
    Assert.Equal(90, negatives.Count)

[<Theory>]
[<MemberData(nameof negatives)>]
let ``every negative fixture fails closed with its expected primary code`` (path: string, expectedCode: string) =
    let fixture = parseFile (Path.Combine(fixtures, path))
    match evaluate fixture with
    | Ok() -> failwithf "%s was accepted; expected %s" path expectedCode
    | Error e ->
        Assert.Equal(expectedCode, e.Code)
        match field "expectedDomainCode" fixture with
        | Some(JStr domain) -> Assert.Equal(Some domain, e.DomainCode)
        | _ -> ()
