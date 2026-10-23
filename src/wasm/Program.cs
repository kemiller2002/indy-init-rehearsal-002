// The .NET WebAssembly export for the Causa engine: forwards one serialized Limen message and returns the
// serialized response. Marshalling plus the single engine-state cell; every decision is the engine's.

using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Causa.Engine;

return;

[SupportedOSPlatform("browser")]
public static partial class CausaEngine
{
    // The only mutable cell: the engine state between WASM calls. It holds the credential in memory only.
    private static App state = AppModule.initial;

    [JSExport]
    internal static string Dispatch(string messageJson)
    {
        // Entropy and time are supplied here, at the boundary, so Shell.dispatch stays pure.
        var stamped = Shell.withContext(Guid.NewGuid(), DateTimeOffset.UtcNow, state);
        var result = Shell.dispatch(stamped, messageJson);
        state = result.Item1;
        return result.Item2;
    }
}
