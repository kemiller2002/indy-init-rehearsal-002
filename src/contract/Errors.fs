namespace Causa.Contract

/// A contract-level refusal (CONTRACT §11). `Code` is stable and machine-readable;
/// `DomainCode` carries the engine's History code for semantic refusals.
type CausaError =
    { Code: string
      DomainCode: string option
      Detail: string }

[<RequireQualifiedAccess>]
module Codes =
    let encoding = "CAUSA.ENCODING"
    let jsonMalformed = "CAUSA.JSON_MALFORMED"
    let nonCanonical = "CAUSA.NON_CANONICAL"
    let unsupportedVersion = "CAUSA.UNSUPPORTED_VERSION"
    let unknownEventType = "CAUSA.UNKNOWN_EVENT_TYPE"
    let schemaViolation = "CAUSA.SCHEMA_VIOLATION"
    let manifestMismatch = "CAUSA.MANIFEST_MISMATCH"
    let eventsHashMismatch = "CAUSA.EVENTS_HASH_MISMATCH"
    let investigationMismatch = "CAUSA.INVESTIGATION_MISMATCH"
    let eventIdDuplicate = "CAUSA.EVENT_ID_DUPLICATE"
    let sequenceInvalid = "CAUSA.SEQUENCE_INVALID"
    let eventIdMismatch = "CAUSA.EVENT_ID_MISMATCH"
    let correlationReused = "CAUSA.CORRELATION_REUSED"
    let historyInadmissible = "CAUSA.HISTORY_INADMISSIBLE"
    let snapshotHashMismatch = "CAUSA.SNAPSHOT_HASH_MISMATCH"
    let snapshotMismatch = "CAUSA.SNAPSHOT_MISMATCH"
    let fixtureDrift = "CAUSA.FIXTURE_DRIFT"
    let goldenMismatch = "CAUSA.GOLDEN_MISMATCH"
    let versionStale = "CAUSA.VERSION_STALE"
    let historyRewritten = "CAUSA.HISTORY_REWRITTEN"
    let pathInvalid = "CAUSA.PATH_INVALID"
    let unrepresentable = "CAUSA.UNREPRESENTABLE"

    /// CONTRACT §11.1 check order: a reader reports the first failing stage as the primary code.
    let order =
        [ pathInvalid; encoding; jsonMalformed; nonCanonical; unsupportedVersion; unknownEventType; schemaViolation
          manifestMismatch; eventsHashMismatch; investigationMismatch; eventIdDuplicate; sequenceInvalid; eventIdMismatch; correlationReused
          historyInadmissible; snapshotHashMismatch; snapshotMismatch; fixtureDrift; goldenMismatch; versionStale; historyRewritten
          unrepresentable ]

[<RequireQualifiedAccess>]
module CausaError =
    let create code detail = { Code = code; DomainCode = None; Detail = detail }
    let fail code detail : Result<'T, CausaError> = Error(create code detail)
