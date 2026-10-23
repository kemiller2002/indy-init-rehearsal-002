namespace Causa.Contract

open System
open System.Globalization
open System.Security.Cryptography
open System.Text
open System.Text.Json

/// The JSON value space the contract admits (CONTRACT §2): no floating point.
type JValue =
    | JNull
    | JBool of bool
    | JInt of int64
    | JStr of string
    | JArr of JValue list
    | JObj of (string * JValue) list

[<AutoOpen>]
module ResultBuilder =
    type ResultBuilder() =
        member _.Bind(r: Result<'A, 'E>, f: 'A -> Result<'B, 'E>) = Result.bind f r
        member _.Return(x: 'A) : Result<'A, 'E> = Ok x
        member _.ReturnFrom(r: Result<'A, 'E>) = r
        member _.Zero() : Result<unit, 'E> = Ok()

    let result = ResultBuilder()

    /// First error wins; order is preserved.
    let rec traverse (f: 'A -> Result<'B, 'E>) (xs: 'A list) : Result<'B list, 'E> =
        match xs with
        | [] -> Ok []
        | x :: rest ->
            match f x with
            | Error e -> Error e
            | Ok y -> traverse f rest |> Result.map (fun ys -> y :: ys)

[<RequireQualifiedAccess>]
module Json =
    /// Largest integer exactly representable in IEEE-754 double (RFC 8785 / I-JSON).
    let maxSafeInteger = 9007199254740991L

    let strictUtf8 = UTF8Encoding(false, true)

    let field (name: string) (o: (string * JValue) list) = o |> List.tryFind (fst >> (=) name) |> Option.map snd

    let private isHighSurrogate (c: char) = c >= '\uD800' && c <= '\uDBFF'
    let private isLowSurrogate (c: char) = c >= '\uDC00' && c <= '\uDFFF'

    /// True when the UTF-16 string contains no unpaired surrogate.
    let isWellFormed (s: string) =
        let rec go i =
            if i >= s.Length then true
            elif isHighSurrogate s[i] then i + 1 < s.Length && isLowSurrogate s[i + 1] && go (i + 2)
            elif isLowSurrogate s[i] then false
            else go (i + 1)
        go 0

    let private integerText = RegularExpressions.Regex(@"^-?(0|[1-9][0-9]*)\z", RegularExpressions.RegexOptions.CultureInvariant)

    let rec private ofElement (path: string) (e: JsonElement) : Result<JValue, string> =
        match e.ValueKind with
        | JsonValueKind.Null -> Ok JNull
        | JsonValueKind.True -> Ok(JBool true)
        | JsonValueKind.False -> Ok(JBool false)
        | JsonValueKind.String ->
            let s = e.GetString() |> nonNull
            if not (isWellFormed s) then Error(path + ": string contains an unpaired surrogate")
            elif s.Contains '\uFEFF' then Error(path + ": U+FEFF is not allowed (CONTRACT §2.3)")
            else Ok(JStr s)
        | JsonValueKind.Number ->
            let raw = e.GetRawText()
            if not (integerText.IsMatch raw) then Error(path + ": number '" + raw + "' is not a JSON integer")
            else
                match Int64.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) with
                | true, v when v >= -maxSafeInteger && v <= maxSafeInteger -> Ok(JInt v)
                | _ -> Error(path + ": integer '" + raw + "' is outside the safe integer range")
        | JsonValueKind.Array ->
            e.EnumerateArray() |> List.ofSeq |> List.mapi (fun i x -> path + "[" + string i + "]", x)
            |> traverse (fun (p, x) -> ofElement p x)
            |> Result.map JArr
        | JsonValueKind.Object ->
            let props = e.EnumerateObject() |> List.ofSeq |> List.map (fun p -> p.Name, p.Value)
            let names = props |> List.map fst
            match names |> List.countBy id |> List.tryFind (fun (_, n) -> n > 1) with
            | Some(dup, _) -> Error(path + ": duplicate object key '" + dup + "'")
            | None ->
                match names |> List.tryFind (fun n -> not (isWellFormed n) || n.Contains '\uFEFF') with
                | Some _ -> Error(path + ": object key contains an unpaired surrogate or U+FEFF")
                | None ->
                    props |> traverse (fun (k, v) -> ofElement (path + "." + k) v |> Result.map (fun j -> k, j))
                    |> Result.map JObj
        | kind -> Error(path + ": unsupported JSON value kind " + string kind)

    /// Strict parse of one complete JSON text (no comments, trailing commas, NaN, BOM; nesting depth <= 128, CONTRACT §2.3).
    let parse (text: string) : Result<JValue, string> =
        try
            let options = JsonDocumentOptions(AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 128)
            use doc = JsonDocument.Parse(text, options)
            ofElement "$" doc.RootElement
        with
        | :? JsonException as ex -> Error("malformed JSON: " + ex.Message)
        | :? InvalidOperationException as ex -> Error("malformed JSON: " + ex.Message)
        | :? ArgumentException as ex -> Error("malformed JSON: " + ex.Message)

    /// Strict UTF-8 decode; rejects invalid sequences and a leading BOM.
    let decodeUtf8 (bytes: byte[]) : Result<string, string> =
        if bytes.Length >= 3 && bytes[0] = 0xEFuy && bytes[1] = 0xBBuy && bytes[2] = 0xBFuy then Error "UTF-8 byte order mark is not allowed"
        else
            try Ok(strictUtf8.GetString bytes)
            with :? DecoderFallbackException -> Error "content is not valid UTF-8"

    // RFC 8785 string serialization (ECMAScript JSON.stringify rules).
    let private escapeInto (sb: StringBuilder) (s: string) =
        sb.Append('"') |> ignore
        s
        |> Seq.iter (fun c ->
            match c with
            | '"' -> sb.Append("\\\"") |> ignore
            | '\\' -> sb.Append("\\\\") |> ignore
            | '\b' -> sb.Append("\\b") |> ignore
            | '\f' -> sb.Append("\\f") |> ignore
            | '\n' -> sb.Append("\\n") |> ignore
            | '\r' -> sb.Append("\\r") |> ignore
            | '\t' -> sb.Append("\\t") |> ignore
            | c when c < ' ' -> sb.Append("\\u").Append((int c).ToString("x4", CultureInfo.InvariantCulture)) |> ignore
            | c -> sb.Append(c) |> ignore)
        sb.Append('"') |> ignore

    let rec private writeInto (sb: StringBuilder) (v: JValue) =
        match v with
        | JNull -> sb.Append("null") |> ignore
        | JBool true -> sb.Append("true") |> ignore
        | JBool false -> sb.Append("false") |> ignore
        | JInt n -> sb.Append(n.ToString(CultureInfo.InvariantCulture)) |> ignore
        | JStr s -> escapeInto sb s
        | JArr items ->
            sb.Append('[') |> ignore
            items |> List.iteri (fun i x -> (if i > 0 then sb.Append(',') |> ignore); writeInto sb x)
            sb.Append(']') |> ignore
        | JObj props ->
            sb.Append('{') |> ignore
            props
            |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b))
            |> List.iteri (fun i (k, x) ->
                (if i > 0 then sb.Append(',') |> ignore)
                escapeInto sb k
                sb.Append(':') |> ignore
                writeInto sb x)
            sb.Append('}') |> ignore

    let rec private problems (v: JValue) : string list =
        match v with
        | JStr s when not (isWellFormed s) -> [ "string contains an unpaired surrogate" ]
        | JStr s when s.Contains '\uFEFF' -> [ "string contains U+FEFF" ]
        | JInt n when n > maxSafeInteger || n < -maxSafeInteger -> [ "integer outside the safe range" ]
        | JArr items -> items |> List.collect problems
        | JObj props ->
            let dups = props |> List.countBy fst |> List.filter (fun (_, n) -> n > 1) |> List.map (fun (k, _) -> "duplicate key '" + k + "'")
            let bad = props |> List.map fst |> List.filter (isWellFormed >> not) |> List.map (fun _ -> "key contains an unpaired surrogate")
            dups @ bad @ (props |> List.collect (snd >> problems))
        | _ -> []

    /// RFC 8785 JSON Canonicalization Scheme text. Fails on values JCS cannot represent.
    let canonical (v: JValue) : Result<string, string> =
        match problems v with
        | [] ->
            let sb = StringBuilder()
            writeInto sb v
            Ok(sb.ToString())
        | p :: _ -> Error("cannot canonicalize: " + p)

    let canonicalBytes (v: JValue) : Result<byte[], string> = canonical v |> Result.map strictUtf8.GetBytes

    let sha256Hex (bytes: byte[]) = Convert.ToHexStringLower(SHA256.HashData bytes)

    /// SHA-256 of the canonical (JCS) form, without the file's trailing LF.
    let digest (v: JValue) : Result<string, string> = canonicalBytes v |> Result.map sha256Hex

    /// A canonical JSON file: JCS text followed by exactly one LF.
    let fileBytes (v: JValue) : Result<byte[], string> = canonical v |> Result.map (fun t -> strictUtf8.GetBytes(t + "\n"))
