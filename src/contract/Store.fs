namespace Causa.Contract

open System
open System.IO
open IndyInitRootCauseInvestigator.Engine

/// The three authoritative-or-derived files of one investigation directory (CONTRACT §3).
type InvestigationFiles =
    { DirectoryId: string
      Manifest: byte[]
      Events: byte[]
      Snapshot: byte[] }

/// A fully verified investigation.
type Loaded =
    { InvestigationId: Guid
      Events: Event list
      State: Investigation
      StateJson: JValue
      Manifest: JValue
      Snapshot: JValue
      EventsSha256: string
      ManifestSha256: string
      SnapshotSha256: string
      StateSha256: string }

[<RequireQualifiedAccess>]
module Store =
    let fileNames = [ "manifest.json"; "events.ndjson"; "snapshot.json" ]

    let schemaIds =
        let b = "https://github.com/kemiller2002/causa-data/contract/v1.0.0/schemas/"
        {| Envelope = b + "event-envelope.schema.json"
           Manifest = b + "investigation-manifest.schema.json"
           Snapshot = b + "snapshot.schema.json"
           State = b + "state.schema.json"
           Root = b + "root-manifest.schema.json"
           Negative = b + "negative-fixture.schema.json"
           Bundle = b + "schema-bundle.schema.json" |}

    let private fail code detail : Result<'T, CausaError> = CausaError.fail code detail

    /// Load and audit the schema bundle from a directory (every *.schema.json, recursively).
    let loadSchemas (dir: string) : Result<SchemaCheck.SchemaSet, CausaError> =
        Directory.GetFiles(dir, "*.schema.json", SearchOption.AllDirectories)
        |> Array.sort
        |> List.ofArray
        |> traverse (fun path ->
            File.ReadAllBytes path |> Json.decodeUtf8 |> Result.bind Json.parse
            |> Result.mapError (fun e -> CausaError.create Codes.schemaViolation (path + ": " + e)))
        |> Result.bind (fun docs ->
            SchemaCheck.load docs |> Result.mapError (fun es -> CausaError.create Codes.schemaViolation (String.Join("; ", es))))
        |> Result.bind (fun set ->
            match SchemaCheck.unresolvedReferences set with
            | [] -> Ok set
            | es -> fail Codes.schemaViolation (String.Join("; ", es)))

    // ---------------- reading ----------------

    /// Split a canonical JSON file: exactly one JSON text followed by one LF.
    /// CONTRACT §2.2: framing is only "non-empty, no CR, ends with LF". Anything else that is
    /// not exactly canonical (interior or extra LFs, spaces) is reported by the canonical stage.
    let private singleDocument (name: string) (text: string) : Result<string, CausaError> =
        if text.Length = 0 then fail Codes.encoding (name + " is empty")
        elif text.Contains '\r' then fail Codes.encoding (name + " contains CR")
        elif not (text.EndsWith "\n") then fail Codes.encoding (name + " must end with LF")
        else Ok(text.Substring(0, text.Length - 1))

    /// NDJSON: one canonical JSON text per line, every line terminated by LF, no blank lines.
    let private lines (name: string) (text: string) : Result<string list, CausaError> =
        if text.Length = 0 then fail Codes.encoding (name + " is empty")
        elif not (text.EndsWith "\n") then fail Codes.encoding (name + " must end with LF")
        else
            let ls = text.Substring(0, text.Length - 1).Split('\n') |> List.ofArray
            match ls |> List.tryFindIndex (fun l -> l.Length = 0) with
            | Some i -> fail Codes.encoding (name + " line " + string (i + 1) + " is blank")
            | None ->
                if ls |> List.exists (fun l -> l.Contains '\r') then fail Codes.encoding (name + " contains CR") else Ok ls

    let private decodeText (name: string) (bytes: byte[]) =
        Json.decodeUtf8 bytes |> Result.mapError (fun e -> CausaError.create Codes.encoding (name + ": " + e))

    let private parseText (name: string) (text: string) =
        Json.parse text |> Result.mapError (fun e -> CausaError.create Codes.jsonMalformed (name + ": " + e))

    let private requireCanonical (name: string) (text: string) (v: JValue) =
        match Json.canonical v with
        | Ok c when c = text -> Ok()
        | Ok _ -> fail Codes.nonCanonical (name + " is not in RFC 8785 canonical form")
        | Error e -> fail Codes.nonCanonical (name + ": " + e)

    let private versionField (name: string) (v: JValue) =
        match v with
        | JObj o ->
            [ "formatVersion"; "schemaVersion"; "semanticVersion" ]
            |> List.tryPick (fun k ->
                match Json.field k o with
                | Some(JStr s) when s <> "1.0.0" -> Some(name + ": " + k + " '" + s + "' is not supported by a v1.0.0 reader")
                | _ -> None)
            |> Option.map (fail Codes.unsupportedVersion)
            |> Option.defaultValue (Ok())
        | _ -> Ok()

    let private registeredType (name: string) (v: JValue) =
        match v with
        | JObj o ->
            match Json.field "eventType" o with
            | Some(JStr t) when not (List.contains t EventWire.eventTypes) -> fail Codes.unknownEventType (name + ": event type '" + t + "' is not registered in v1.0.0")
            | _ -> Ok()
        | _ -> Ok()

    let private schemaCheck (set: SchemaCheck.SchemaSet) (schemaId: string) (name: string) (v: JValue) =
        match SchemaCheck.check set schemaId v with
        | [] -> Ok()
        | e :: rest -> fail Codes.schemaViolation (name + ": " + e + (if rest.IsEmpty then "" else " (+" + string rest.Length + " more)"))

    let private each (f: int -> 'A -> Result<unit, CausaError>) (xs: 'A list) =
        xs |> List.indexed |> traverse (fun (i, x) -> f i x) |> Result.map ignore

    let private lineName i = "events.ndjson line " + string (i + 1)

    type ParsedEvents = { Texts: string list; Values: JValue list }

    /// Stages encoding..schema for an events file (CONTRACT §11.1 stages 2-7).
    let private parseEvents (set: SchemaCheck.SchemaSet) (bytes: byte[]) : Result<ParsedEvents, CausaError> =
        result {
            let! text = decodeText "events.ndjson" bytes
            let! ls = lines "events.ndjson" text
            let! values = ls |> List.indexed |> traverse (fun (i, l) -> parseText (lineName i) l)
            let pairs = List.zip ls values
            do! pairs |> each (fun i (l, v) -> requireCanonical (lineName i) l v)
            do! values |> each (fun i v -> versionField (lineName i) v)
            do! values |> each (fun i v -> registeredType (lineName i) v)
            do! values |> each (fun i v -> schemaCheck set schemaIds.Envelope (lineName i) v)
            return { Texts = ls; Values = values }
        }

    let private decodeAll (values: JValue list) =
        values |> List.indexed |> traverse (fun (i, v) -> EventWire.decode v |> Result.mapError (fun e -> { e with Detail = lineName i + ": " + e.Detail }))

    /// Stages investigation-id..history over decoded events (CONTRACT §11.1 stages 10-14).
    let private checkHistory (investigation: Guid) (decoded: EventWire.Decoded list) : Result<Investigation, CausaError> =
        result {
            do! match decoded |> List.tryFind (fun d -> d.InvestigationId <> investigation) with
                | Some d -> fail Codes.investigationMismatch ("event " + string d.Event.Sequence + " names investigation " + Uuid.text d.InvestigationId)
                | None -> Ok()
            do! match decoded |> List.countBy (fun d -> d.EventId) |> List.tryFind (fun (_, n) -> n > 1) with
                | Some(id, _) -> fail Codes.eventIdDuplicate ("eventId " + Uuid.text id + " appears more than once")
                | None -> Ok()
            do! match decoded |> List.indexed |> List.tryFind (fun (i, d) -> d.Event.Sequence <> int64 (i + 1)) with
                | Some(i, d) -> fail Codes.sequenceInvalid ("line " + string (i + 1) + " has aggregateVersion " + string d.Event.Sequence + "; expected " + string (i + 1))
                | None -> Ok()
            do! match decoded |> List.tryFind (fun d -> d.EventId <> Uuid.eventId investigation d.Event.Sequence d.Event.CommandId) with
                | Some d -> fail Codes.eventIdMismatch ("event " + string d.Event.Sequence + " eventId is not the derived UUIDv5")
                | None -> Ok()
            // CONTRACT §5.4: a correlationId names exactly one command group.
            do! let groups = decoded |> List.map (fun d -> d.Event.CommandId) |> List.fold (fun acc c -> match acc with h :: _ when h = c -> acc | _ -> c :: acc) []
                match groups |> List.countBy id |> List.tryFind (fun (_, n) -> n > 1) with
                | Some(c, _) -> fail Codes.correlationReused ("correlationId " + Uuid.text c + " names more than one command group")
                | None -> Ok()
            let events = decoded |> List.map (fun d -> d.Event)
            do! match History.validate events with
                | [] -> Ok()
                | first :: _ as all ->
                    Error { Code = Codes.historyInadmissible; DomainCode = Some first.Code
                            Detail = String.Join(" | ", all |> List.map (fun e -> e.Code + ": " + e.Message)) }
            return!
                try
                    match Evolve.replay events with
                    | Some s -> Ok s
                    | None -> fail Codes.historyInadmissible "empty history"
                with ex -> fail Codes.historyInadmissible ("replay failed: " + ex.Message)
        }

    let private eventsDigest (bytes: byte[]) = Json.sha256Hex bytes

    let private manifestChecks (dirId: Guid) (manifest: JValue) (decoded: EventWire.Decoded list) (eventsBytes: byte[]) =
        let o = match manifest with JObj o -> o | _ -> []
        let int k = match Json.field k o with Some(JInt n) -> n | _ -> -1L
        let str k = match Json.field k o with Some(JStr s) -> s | _ -> ""
        let n = int64 decoded.Length
        let firstAt = decoded |> List.head |> fun d -> Timestamp.tryFormat d.Event.At |> Option.defaultValue ""
        let lastAt = decoded |> List.last |> fun d -> Timestamp.tryFormat d.Event.At |> Option.defaultValue ""
        result {
            do! if str "investigationId" = Uuid.text dirId then Ok() else fail Codes.manifestMismatch "manifest investigationId differs from its directory"
            do! if int "aggregateVersion" = n then Ok() else fail Codes.manifestMismatch ("manifest aggregateVersion " + string (int "aggregateVersion") + " but events.ndjson has " + string n + " events")
            do! if int "eventCount" = n then Ok() else fail Codes.manifestMismatch ("manifest eventCount " + string (int "eventCount") + " but events.ndjson has " + string n + " events")
            do! if str "createdAt" = firstAt then Ok() else fail Codes.manifestMismatch "manifest createdAt is not the first event's occurredAt"
            do! if str "updatedAt" = lastAt then Ok() else fail Codes.manifestMismatch "manifest updatedAt is not the last event's occurredAt"
            do! if str "eventsSha256" = eventsDigest eventsBytes then Ok() else fail Codes.eventsHashMismatch "manifest eventsSha256 does not match events.ndjson bytes"
        }

    /// Full verification of one investigation directory, in contract order (CONTRACT §11.1).
    let validate (set: SchemaCheck.SchemaSet) (files: InvestigationFiles) : Result<Loaded, CausaError> =
        result {
            let! dirId =
                match Uuid.tryParse files.DirectoryId with
                | Some g -> Ok g
                | None -> fail Codes.pathInvalid ("investigation directory '" + files.DirectoryId + "' is not a canonical lowercase UUID")
            // Stage: encoding (all files)
            let! manifestText = decodeText "manifest.json" files.Manifest |> Result.bind (singleDocument "manifest.json")
            let! snapshotText = decodeText "snapshot.json" files.Snapshot |> Result.bind (singleDocument "snapshot.json")
            let! eventsText = decodeText "events.ndjson" files.Events
            let! eventLines = lines "events.ndjson" eventsText
            // Stage: JSON syntax (all files)
            let! manifest = parseText "manifest.json" manifestText
            let! snapshot = parseText "snapshot.json" snapshotText
            let! eventValues = eventLines |> List.indexed |> traverse (fun (i, l) -> parseText (lineName i) l)
            // Stage: canonical bytes
            do! requireCanonical "manifest.json" manifestText manifest
            do! requireCanonical "snapshot.json" snapshotText snapshot
            do! List.zip eventLines eventValues |> each (fun i (l, v) -> requireCanonical (lineName i) l v)
            // Stage: versions, registered event types, schemas
            do! versionField "manifest.json" manifest
            do! versionField "snapshot.json" snapshot
            do! eventValues |> each (fun i v -> versionField (lineName i) v)
            do! eventValues |> each (fun i v -> registeredType (lineName i) v)
            do! schemaCheck set schemaIds.Manifest "manifest.json" manifest
            do! eventValues |> each (fun i v -> schemaCheck set schemaIds.Envelope (lineName i) v)
            do! schemaCheck set schemaIds.Snapshot "snapshot.json" snapshot
            let! decoded = decodeAll eventValues
            // Stage: manifest consistency and integrity
            do! manifestChecks dirId manifest decoded files.Events
            // Stage: identity, ordering, admissibility
            let! state = checkHistory dirId decoded
            // Stage: derived snapshot
            let! snapshotSha = Json.digest snapshot |> Result.mapError (CausaError.create Codes.nonCanonical)
            let manifestSnapshotSha = match manifest with JObj o -> (match Json.field "snapshotSha256" o with Some(JStr s) -> s | _ -> "") | _ -> ""
            do! if manifestSnapshotSha = snapshotSha then Ok() else fail Codes.snapshotHashMismatch "manifest snapshotSha256 does not match snapshot.json"
            let events = decoded |> List.map (fun d -> d.Event)
            let! expected = Projection.snapshot state (List.last events).At
            let! expectedText = Json.canonical expected |> Result.mapError (CausaError.create Codes.unrepresentable)
            do! if expectedText = snapshotText then Ok() else fail Codes.snapshotMismatch "snapshot.json is not the projection of the replayed event history"
            let stateJson = match expected with JObj o -> Json.field "state" o |> Option.defaultValue JNull | _ -> JNull
            let! stateSha = Json.digest stateJson |> Result.mapError (CausaError.create Codes.unrepresentable)
            let! manifestSha = Json.digest manifest |> Result.mapError (CausaError.create Codes.nonCanonical)
            return
                { InvestigationId = dirId; Events = events; State = state; StateJson = stateJson; Manifest = manifest; Snapshot = snapshot
                  EventsSha256 = eventsDigest files.Events; ManifestSha256 = manifestSha; SnapshotSha256 = snapshotSha; StateSha256 = stateSha }
        }

    // ---------------- writing ----------------

    let private encodeAll (events: Event list) =
        events |> traverse EventWire.encode |> Result.bind (traverse (Json.canonical >> Result.mapError (CausaError.create Codes.unrepresentable)))

    /// Encode a history into files WITHOUT verifying admissibility. Used only to construct
    /// negative fixtures; every real writer goes through `build`.
    let buildUnchecked (events: Event list) : Result<InvestigationFiles, CausaError> =
        result {
            let! first = match events with e :: _ -> Ok e | [] -> fail Codes.unrepresentable "an investigation needs at least one event"
            let last = List.last events
            let! texts = encodeAll events
            let eventsBytes = Json.strictUtf8.GetBytes(String.Join("", texts |> List.map (fun t -> t + "\n")))
            let! state =
                try
                    match Evolve.replay events with
                    | Some s -> Ok s
                    | None -> fail Codes.unrepresentable "empty history"
                with ex -> fail Codes.historyInadmissible ("replay failed: " + ex.Message)
            let! snapshot = Projection.snapshot state last.At
            let! snapshotSha = Json.digest snapshot |> Result.mapError (CausaError.create Codes.unrepresentable)
            let! createdAt = Fields.ts "createdAt" first.At
            let! updatedAt = Fields.ts "updatedAt" last.At
            let manifest =
                JObj
                    [ "formatVersion", JStr Contract.formatVersion
                      "investigationId", JStr(Uuid.text first.Investigation.Value)
                      "schemaVersion", JStr Contract.schemaVersion
                      "semanticVersion", JStr Contract.semanticVersion
                      "aggregateVersion", JInt last.Sequence
                      "eventCount", JInt(int64 events.Length)
                      "eventsSha256", JStr(eventsDigest eventsBytes)
                      "snapshotVersion", JInt Contract.snapshotVersion
                      "snapshotSha256", JStr snapshotSha
                      "domainPacks", JArr []
                      "createdAt", createdAt
                      "updatedAt", updatedAt ]
            let! manifestBytes = Json.fileBytes manifest |> Result.mapError (CausaError.create Codes.unrepresentable)
            let! snapshotBytes = Json.fileBytes snapshot |> Result.mapError (CausaError.create Codes.unrepresentable)
            return { DirectoryId = Uuid.text first.Investigation.Value; Manifest = manifestBytes; Events = eventsBytes; Snapshot = snapshotBytes }
        }

    /// Build the canonical file set for a complete history. The result is re-verified through
    /// `validate` so a writer can never emit something a conforming reader rejects.
    let build (set: SchemaCheck.SchemaSet) (events: Event list) : Result<InvestigationFiles * Loaded, CausaError> =
        buildUnchecked events |> Result.bind (fun files -> validate set files |> Result.map (fun loaded -> files, loaded))

    /// Optimistic append (CONTRACT §9.2): the caller's expected version must equal the
    /// current aggregate version; the combined history is fully re-verified.
    let append (set: SchemaCheck.SchemaSet) (current: Loaded) (expectedVersion: int64) (newEvents: Event list) =
        if expectedVersion <> current.State.Version then
            fail Codes.versionStale ("expected aggregate version " + string expectedVersion + " but the investigation is at " + string current.State.Version)
        elif newEvents.IsEmpty then fail Codes.unrepresentable "an append needs at least one event"
        else build set (current.Events @ newEvents)

    /// Append pre-encoded NDJSON lines (as received from an untrusted writer) to a verified investigation.
    let appendLines (set: SchemaCheck.SchemaSet) (current: Loaded) (expectedVersion: int64) (appended: byte[]) =
        if expectedVersion <> current.State.Version then
            fail Codes.versionStale ("expected aggregate version " + string expectedVersion + " but the investigation is at " + string current.State.Version)
        else
            result {
                let! parsed = parseEvents set appended
                let! decoded = decodeAll parsed.Values
                let! currentDecoded =
                    current.Events |> traverse (fun e -> EventWire.encode e |> Result.bind EventWire.decode)
                let! _ = checkHistory current.InvestigationId (currentDecoded @ decoded)
                return! build set (current.Events @ (decoded |> List.map (fun d -> d.Event)))
            }

    /// A successor file set must extend the accepted history byte-for-byte (CONTRACT §9.3).
    let successor (set: SchemaCheck.SchemaSet) (current: Loaded) (currentEventsBytes: byte[]) (candidate: InvestigationFiles) =
        result {
            let! loaded = validate set candidate
            let prefix = candidate.Events.Length >= currentEventsBytes.Length && Array.sub candidate.Events 0 currentEventsBytes.Length = currentEventsBytes
            do! if loaded.InvestigationId = current.InvestigationId && prefix then Ok()
                else fail Codes.historyRewritten "the candidate history does not extend the accepted history byte-for-byte"
            return loaded
        }

    /// CONTRACT §3.2-§3.3: a canonical directory name and exactly the three files. Runs before any
    /// file is read so that path problems are always the primary code (stage 1).
    let ofFileMap (directoryId: string) (files: Map<string, byte[]>) : Result<InvestigationFiles, CausaError> =
        match Uuid.tryParse directoryId with
        | None -> fail Codes.pathInvalid ("investigation directory '" + directoryId + "' is not a canonical lowercase UUID")
        | Some _ ->
            let names = files |> Map.toList |> List.map fst |> List.sort
            if names <> List.sort fileNames then
                fail Codes.pathInvalid ("investigation directory must contain exactly " + String.Join(", ", fileNames) + "; found: " + String.Join(", ", names))
            else Ok { DirectoryId = directoryId; Manifest = files["manifest.json"]; Events = files["events.ndjson"]; Snapshot = files["snapshot.json"] }

    /// Read a directory; every entry counts, including hidden files and subdirectories.
    let readDirectory (dir: string) : Result<InvestigationFiles, CausaError> =
        let id = Path.GetFileName(Path.TrimEndingDirectorySeparator dir) |> nonNull
        let entries = Directory.GetFileSystemEntries dir |> List.ofArray
        let subdirs = entries |> List.filter Directory.Exists
        if not subdirs.IsEmpty then fail Codes.pathInvalid ("investigation directory contains a subdirectory: " + (Path.GetFileName(subdirs.Head) |> nonNull))
        else entries |> List.map (fun p -> (Path.GetFileName p |> nonNull), File.ReadAllBytes p) |> Map.ofList |> ofFileMap id
