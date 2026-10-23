namespace Causa.Engine

open Aegis

module Operational =
    let aegis = Aegis.configure "causa" None [ Sinks.standardError ]

    let validateConfiguration () =
        Bootstrap.validate None aegis
