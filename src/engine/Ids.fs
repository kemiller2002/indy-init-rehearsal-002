namespace Causa.Engine

open System
open System.Security.Cryptography

/// Entropy and time enter the engine only through the WASM export (one seed and one instant per message), so every
/// transition stays pure and replayable in tests.
[<RequireQualifiedAccess>]
module Ids =
    /// The n-th identifier derived from a boundary seed: SHA-256(seed ‖ n), shaped as a version-4 UUID.
    let derive (seed: Guid) (n: int) : Guid =
        let digest = SHA256.HashData(Array.append (seed.ToByteArray()) (BitConverter.GetBytes n))
        digest
        |> Array.take 16
        |> Array.mapi (fun i b ->
            match i with
            | 7 -> (b &&& 0x0Fuy) ||| 0x40uy
            | 8 -> (b &&& 0x3Fuy) ||| 0x80uy
            | _ -> b)
        |> Guid

    /// The contract requires millisecond-truncated UTC at the command boundary (CONTRACT §2.9).
    let truncate (instant: DateTimeOffset) : DateTimeOffset =
        let ticks = instant.UtcTicks
        DateTimeOffset(ticks - ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero)

    let text (id: Guid) = id.ToString("D")

/// What the boundary supplies with every message.
type Context = { Now: DateTimeOffset; Seed: Guid }

[<RequireQualifiedAccess>]
module Context =
    let initial = { Now = DateTimeOffset.UnixEpoch; Seed = Guid.Empty }
