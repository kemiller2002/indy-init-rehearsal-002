namespace Causa.Engine

open System.IO
open Causa.Contract

/// The normative Causa v1.0.0 schemas, embedded in Causa.Contract at build time (they are the contract, not
/// data). Loaded once; every read and every write is checked against this set (EXT-DATA-005, EXT-DATA-011).
[<RequireQualifiedAccess>]
module Schemas =
    let private prefix = "causa-schemas/"

    let private documents () =
        let assembly = typeof<InvestigationFiles>.Assembly
        assembly.GetManifestResourceNames()
        |> Array.filter (fun name -> name.StartsWith(prefix, System.StringComparison.Ordinal) && name.EndsWith(".schema.json", System.StringComparison.Ordinal))
        |> Array.sort
        |> Array.toList
        |> List.map (fun name ->
            use stream = assembly.GetManifestResourceStream name |> nonNull
            use memory = new MemoryStream()
            stream.CopyTo memory
            name, memory.ToArray())

    let private load () : Result<SchemaCheck.SchemaSet, CausaError> =
        let parse (name: string, bytes: byte[]) =
            bytes |> Json.decodeUtf8 |> Result.bind Json.parse |> Result.mapError (fun e -> CausaError.create Codes.schemaViolation (name + ": " + e))
        let rec traverse acc items =
            match items with
            | [] -> Ok(List.rev acc)
            | x :: rest -> parse x |> Result.bind (fun v -> traverse (v :: acc) rest)
        match documents () with
        | [] -> CausaError.fail Codes.schemaViolation "no embedded schemas"
        | docs ->
            traverse [] docs
            |> Result.bind (fun parsed -> SchemaCheck.load parsed |> Result.mapError (fun es -> CausaError.create Codes.schemaViolation (System.String.Join("; ", es))))
            |> Result.bind (fun set ->
                match SchemaCheck.unresolvedReferences set with
                | [] -> Ok set
                | es -> CausaError.fail Codes.schemaViolation (System.String.Join("; ", es)))

    /// The schema set, or the reason it could not be loaded (fail closed: no reads or writes without it).
    let set : Lazy<Result<SchemaCheck.SchemaSet, CausaError>> = lazy (load ())

    /// Number of embedded schema documents (a build-integrity check).
    let count () = documents () |> List.length
