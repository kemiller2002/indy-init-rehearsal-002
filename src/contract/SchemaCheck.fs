namespace Causa.Contract

open System
open System.Text.RegularExpressions

/// A deliberately small Draft 2020-12 interpreter for the keyword subset the v1 schemas use.
/// It fails closed: any keyword outside the subset makes the schema set unusable, so a
/// schema can never contain a constraint that this toolkit silently ignores (CONTRACT §10).
[<RequireQualifiedAccess>]
module SchemaCheck =
    let supported =
        set [ "$schema"; "$id"; "$ref"; "$defs"; "$comment"; "title"; "description"; "type"; "const"; "enum"; "properties"
              "required"; "additionalProperties"; "items"; "minItems"; "maxItems"; "uniqueItems"; "minLength"; "maxLength"
              "pattern"; "minimum"; "maximum"; "oneOf"; "anyOf"; "allOf"; "if"; "then"; "else"; "not" ]

    type SchemaSet = { Documents: Map<string, JValue> }

    let private obj (v: JValue) = match v with JObj o -> Some o | _ -> None

    /// Every keyword at every schema position must be in the supported subset.
    let rec private audit (path: string) (schema: JValue) : string list =
        match schema with
        | JBool _ -> []
        | JObj o ->
            let own = o |> List.map fst |> List.filter (supported.Contains >> not) |> List.map (fun k -> path + ": unsupported keyword '" + k + "'")
            let sub (k: string) =
                match Json.field k o with
                | None -> []
                | Some(JObj props) when k = "properties" || k = "$defs" -> props |> List.collect (fun (n, s) -> audit (path + "/" + k + "/" + n) s)
                | Some(JArr xs) when k = "oneOf" || k = "anyOf" || k = "allOf" -> xs |> List.mapi (fun i s -> audit (path + "/" + k + "/" + string i) s) |> List.concat
                | Some s when k = "items" || k = "additionalProperties" || k = "if" || k = "then" || k = "else" || k = "not" -> audit (path + "/" + k) s
                | Some _ -> []
            own @ ([ "properties"; "$defs"; "oneOf"; "anyOf"; "allOf"; "items"; "additionalProperties"; "if"; "then"; "else"; "not" ] |> List.collect sub)
        | _ -> [ path + ": a schema must be an object or boolean" ]

    let private regexCache = Collections.Concurrent.ConcurrentDictionary<string, Regex>()

    /// JSON Schema patterns are ECMA-262: a final `$` matches only at the very end of the
    /// string. .NET's `$` also matches before a trailing LF, so translate it to `\z`.
    let private dotnetPattern (p: string) =
        if p.EndsWith "$" && not (p.EndsWith "\\$") then p.Substring(0, p.Length - 1) + "\\z" else p

    let private regex (p: string) = regexCache.GetOrAdd(p, fun p -> Regex(dotnetPattern p, RegexOptions.CultureInvariant))

    /// Patterns may use `$` only as the final anchor (fail closed on anything else).
    let private patternProblems (path: string) (schema: JValue) =
        let rec walk (v: JValue) =
            match v with
            | JObj o ->
                (o |> List.choose (function
                    | "pattern", JStr p when p.TrimEnd('$').Replace("\\$", "").Contains '$' -> Some(path + ": pattern '" + p + "' uses '$' other than as the final anchor")
                    | _ -> None))
                @ (o |> List.collect (snd >> walk))
            | JArr xs -> xs |> List.collect walk
            | _ -> []
        walk schema

    let load (documents: JValue list) : Result<SchemaSet, string list> =
        let ids =
            documents
            |> List.map (fun d ->
                match obj d |> Option.bind (Json.field "$id") with
                | Some(JStr id) -> Ok(id, d)
                | _ -> Error "schema without a string $id")
        match ids |> List.choose (function Error e -> Some e | _ -> None) with
        | (_ :: _) as errors -> Error errors
        | [] ->
            let pairs = ids |> List.choose (function Ok p -> Some p | _ -> None)
            let problems = pairs |> List.collect (fun (id, d) -> audit id d @ patternProblems id d)
            if problems.IsEmpty then Ok { Documents = Map.ofList pairs } else Error problems

    /// Resolve "file.schema.json#/$defs/name" (relative to `baseId`) to (documentId, schema).
    let resolve (set: SchemaSet) (baseId: string) (reference: string) : Result<string * JValue, string> =
        let absolute = Uri(Uri(baseId), reference)
        let docId = absolute.GetLeftPart(UriPartial.Query)
        let fragment = Uri.UnescapeDataString(absolute.Fragment.TrimStart('#'))
        match set.Documents |> Map.tryFind docId with
        | None -> Error("unresolved $ref '" + reference + "' from " + baseId)
        | Some doc ->
            let segments = fragment.Split('/', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray
            let rec walk (node: JValue) segs =
                match segs, node with
                | [], n -> Ok n
                | s :: rest, JObj o ->
                    match Json.field s o with
                    | Some n -> walk n rest
                    | None -> Error("unresolved $ref fragment '" + fragment + "' in " + docId)
                | _ -> Error("unresolved $ref fragment '" + fragment + "' in " + docId)
            walk doc segments |> Result.map (fun s -> docId, s)

    /// Every $ref in every document must resolve (Gate 3: no unresolved references).
    let unresolvedReferences (set: SchemaSet) : string list =
        let rec refs (v: JValue) =
            match v with
            | JObj o -> (o |> List.choose (function "$ref", JStr r -> Some r | _ -> None)) @ (o |> List.collect (snd >> refs))
            | JArr xs -> xs |> List.collect refs
            | _ -> []
        set.Documents
        |> Map.toList
        |> List.collect (fun (id, d) -> refs d |> List.choose (fun r -> match resolve set id r with Error e -> Some e | Ok _ -> None))

    let private typeName (v: JValue) =
        match v with
        | JNull -> "null" | JBool _ -> "boolean" | JInt _ -> "integer" | JStr _ -> "string" | JArr _ -> "array" | JObj _ -> "object"

    let private hasType (v: JValue) (t: string) =
        match t, v with
        | "number", JInt _ -> true
        | t, v -> typeName v = t

    let private canonicalOf v = Json.canonical v |> Result.defaultValue "<non-canonical>"


    /// JSON Schema string length counts Unicode code points, not UTF-16 units.
    let private codePoints (s: string) = s.EnumerateRunes() |> Seq.length

    let rec validate (set: SchemaSet) (baseId: string) (schema: JValue) (path: string) (v: JValue) : string list =
        match schema with
        | JBool true -> []
        | JBool false -> [ path + ": no value is allowed here" ]
        | JObj s ->
            let kw k = Json.field k s
            let here msg = [ path + ": " + msg ]
            let refErrors =
                match kw "$ref" with
                | Some(JStr r) ->
                    match resolve set baseId r with
                    | Ok(docId, target) ->
                        let structural = validate set docId target path v
                        // CONTRACT §2.9: the `timestamp` definition also requires a real calendar instant.
                        let calendar =
                            match v with
                            | JStr s when structural.IsEmpty && docId.EndsWith "/common.schema.json" && Uri(Uri(baseId), r).Fragment = "#/$defs/timestamp" && (Timestamp.tryParse s).IsNone ->
                                here ("'" + s + "' is not a real calendar instant")
                            | _ -> []
                        structural @ calendar
                    | Error e -> here e
                | _ -> []
            let typeErrors =
                match kw "type" with
                | Some(JStr t) -> if hasType v t then [] else here ("expected " + t + ", found " + typeName v)
                | Some(JArr ts) ->
                    let names = ts |> List.choose (function JStr t -> Some t | _ -> None)
                    if names |> List.exists (hasType v) then [] else here ("expected one of " + String.Join("|", names) + ", found " + typeName v)
                | _ -> []
            let constErrors =
                match kw "const" with
                | Some c -> if canonicalOf c = canonicalOf v then [] else here ("must equal " + canonicalOf c)
                | None -> []
            let enumErrors =
                match kw "enum" with
                | Some(JArr xs) -> if xs |> List.exists (fun x -> canonicalOf x = canonicalOf v) then [] else here ("value " + canonicalOf v + " is not in the enumeration")
                | _ -> []
            let objectErrors =
                match v with
                | JObj o ->
                    let props = match kw "properties" with Some(JObj p) -> p | _ -> []
                    let required =
                        match kw "required" with
                        | Some(JArr r) -> r |> List.choose (function JStr n -> Some n | _ -> None) |> List.filter (fun n -> Json.field n o |> Option.isNone)
                        | _ -> []
                        |> List.map (fun n -> path + ": missing required property '" + n + "'")
                    let declared = o |> List.collect (fun (k, x) -> match Json.field k props with Some ps -> validate set baseId ps (path + "." + k) x | None -> [])
                    let additional =
                        match kw "additionalProperties" with
                        | Some extra ->
                            o |> List.filter (fun (k, _) -> Json.field k props |> Option.isNone)
                            |> List.collect (fun (k, x) ->
                                match extra with
                                | JBool false -> [ path + ": property '" + k + "' is not allowed" ]
                                | e -> validate set baseId e (path + "." + k) x)
                        | None -> []
                    required @ declared @ additional
                | _ -> []
            let arrayErrors =
                match v with
                | JArr xs ->
                    let items = match kw "items" with Some i -> xs |> List.mapi (fun n x -> validate set baseId i (path + "[" + string n + "]") x) |> List.concat | None -> []
                    let minItems = match kw "minItems" with Some(JInt n) when int64 xs.Length < n -> here ("expected at least " + string n + " items") | _ -> []
                    let maxItems = match kw "maxItems" with Some(JInt n) when int64 xs.Length > n -> here ("expected at most " + string n + " items") | _ -> []
                    let unique =
                        match kw "uniqueItems" with
                        | Some(JBool true) when (xs |> List.map canonicalOf |> List.distinct |> List.length) <> xs.Length -> here "items must be unique"
                        | _ -> []
                    items @ minItems @ maxItems @ unique
                | _ -> []
            let stringErrors =
                match v with
                | JStr str ->
                    let n = codePoints str
                    let minL = match kw "minLength" with Some(JInt m) when int64 n < m -> here ("shorter than " + string m) | _ -> []
                    let maxL = match kw "maxLength" with Some(JInt m) when int64 n > m -> here ("longer than " + string m) | _ -> []
                    let pat = match kw "pattern" with Some(JStr p) when not ((regex p).IsMatch str) -> here ("does not match pattern " + p) | _ -> []
                    minL @ maxL @ pat
                | _ -> []
            let numberErrors =
                match v with
                | JInt n ->
                    let lo = match kw "minimum" with Some(JInt m) when n < m -> here ("below minimum " + string m) | _ -> []
                    let hi = match kw "maximum" with Some(JInt m) when n > m -> here ("above maximum " + string m) | _ -> []
                    lo @ hi
                | _ -> []
            let sub (k: string) = match kw k with Some(JArr xs) -> xs | _ -> []
            let passes s = validate set baseId s path v |> List.isEmpty
            let allOf = sub "allOf" |> List.collect (fun s -> validate set baseId s path v)
            let anyOf = match sub "anyOf" with [] -> [] | xs -> if xs |> List.exists passes then [] else here "matches none of anyOf"
            let oneOf =
                match sub "oneOf" with
                | [] -> []
                | xs ->
                    match xs |> List.filter passes |> List.length with
                    | 1 -> []
                    | 0 ->
                        // Report the closest branch's errors to keep messages actionable.
                        let best = xs |> List.map (fun s -> validate set baseId s path v) |> List.minBy List.length
                        here "matches none of oneOf" @ best
                    | n -> here ("matches " + string n + " branches of oneOf")
            let conditional =
                match kw "if" with
                | Some cond ->
                    if passes cond then (match kw "then" with Some t -> validate set baseId t path v | None -> [])
                    else (match kw "else" with Some e -> validate set baseId e path v | None -> [])
                | None -> []
            let negation = match kw "not" with Some n when passes n -> here "matches a forbidden schema" | _ -> []
            refErrors @ typeErrors @ constErrors @ enumErrors @ objectErrors @ arrayErrors @ stringErrors @ numberErrors
            @ allOf @ anyOf @ oneOf @ conditional @ negation
        | _ -> [ path + ": invalid schema node" ]

    /// Validate a document against a root schema identified by its $id.
    let check (set: SchemaSet) (schemaId: string) (v: JValue) : string list =
        match set.Documents |> Map.tryFind schemaId with
        | Some s -> validate set schemaId s "$" v
        | None -> [ "schema '" + schemaId + "' is not in the bundle" ]
