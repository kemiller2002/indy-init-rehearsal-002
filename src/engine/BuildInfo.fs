namespace Causa.Engine

open System.Reflection

/// The build identity, stamped by the build (`-p:SourceRevisionId=<commit>`), so the running application can show
/// exactly which source it was built from (G9, DCC-008).
[<RequireQualifiedAccess>]
module BuildInfo =
    type private Marker = class end

    let private informational =
        match typeof<Marker>.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
        | null -> "0.0.0"
        | attribute -> attribute.InformationalVersion

    let version = match informational.IndexOf '+' with -1 -> informational | i -> informational.Substring(0, i)
    let commit = match informational.IndexOf '+' with -1 -> "unknown" | i -> informational.Substring(i + 1)
