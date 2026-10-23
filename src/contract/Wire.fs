namespace Causa.Contract

open System
open System.Globalization
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions
open IndyInitRootCauseInvestigator.Engine

/// Contract identity constants (CONTRACT §1).
[<RequireQualifiedAccess>]
module Contract =
    let formatVersion = "1.0.0"
    let schemaVersion = "1.0.0"
    let semanticVersion = "1.0.0"
    let projectionVersion = 1L
    let snapshotVersion = 1L
    let datasetId = "causa-demo-v1"

/// RFC 4122 §4.3 name-based UUIDs (version 5, SHA-1) and canonical UUID text.
[<RequireQualifiedAccess>]
module Uuid =
    let private canonicalText = Regex(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z", RegexOptions.CultureInvariant)

    let text (g: Guid) = g.ToString("D")

    /// Lowercase, hyphenated, 36 characters, not the nil UUID.
    let tryParse (s: string) : Guid option =
        if canonicalText.IsMatch s then
            match Guid.TryParse s with
            | true, g when g <> Guid.Empty -> Some g
            | _ -> None
        else None

    let v5 (ns: Guid) (name: string) : Guid =
        let input = Array.append (ns.ToByteArray(true)) (Encoding.UTF8.GetBytes name)
        let hash = SHA1.HashData input
        let bytes =
            hash
            |> Array.take 16
            |> Array.mapi (fun i b ->
                match i with
                | 6 -> (b &&& 0x0Fuy) ||| 0x50uy
                | 8 -> (b &&& 0x3Fuy) ||| 0x80uy
                | _ -> b)
        Guid(ReadOnlySpan<byte>(bytes), true)

    /// RFC 4122 Appendix C name space for URLs.
    let urlNamespace = Guid.Parse "6ba7b811-9dad-11d1-80b4-00c04fd430c8"

    /// CONTRACT §5.2: namespace of derived event identifiers.
    let eventNamespace = v5 urlNamespace "https://github.com/kemiller2002/causa-data/contract/v1/event-id"

    /// CONTRACT §5.2: eventId = UUIDv5(eventNamespace, "<investigationId>/<aggregateVersion>/<correlationId>").
    let eventId (investigation: Guid) (aggregateVersion: int64) (correlation: Guid) =
        v5 eventNamespace (text investigation + "/" + aggregateVersion.ToString(CultureInfo.InvariantCulture) + "/" + text correlation)

/// CONTRACT §2.4: RFC 3339 UTC timestamps with exactly millisecond precision.
[<RequireQualifiedAccess>]
module Timestamp =
    let private format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'"

    /// None when the instant has sub-millisecond precision (it cannot be represented losslessly).
    let tryFormat (d: DateTimeOffset) : string option =
        let utc = d.UtcDateTime
        if utc.Ticks % TimeSpan.TicksPerMillisecond <> 0L then None
        else Some(utc.ToString(format, CultureInfo.InvariantCulture))

    /// CONTRACT §2.9: the command boundary MUST supply millisecond-truncated UTC (e.g. for RequestedAt),
    /// because writers refuse sub-millisecond instants rather than silently changing them.
    let truncateToMillisecond (d: DateTimeOffset) =
        let utc = d.UtcDateTime
        DateTimeOffset(DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc))

    let tryParse (s: string) : DateTimeOffset option =
        match DateTime.TryParseExact(s, format, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal ||| DateTimeStyles.AssumeUniversal) with
        | true, d -> Some(DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)))
        | _ -> None

/// A bidirectional, exhaustive wire mapping for one domain union (CONTRACT §6).
type WireEnum<'T when 'T: equality> =
    { Name: string
      ToWire: 'T -> string
      All: 'T list }

[<RequireQualifiedAccess>]
module WireEnum =
    let values (e: WireEnum<'T>) = e.All |> List.map e.ToWire
    let tryParse (e: WireEnum<'T>) (s: string) = e.All |> List.tryFind (fun v -> e.ToWire v = s)

    let make name toWire all = { Name = name; ToWire = toWire; All = all }

/// Every persisted enumeration. `ToWire` is a total match (compiler-checked exhaustive);
/// `All` must list every case (checked by tests against the engine's own case lists).
[<RequireQualifiedAccess>]
module Enums =
    let lifecycle =
        WireEnum.make "lifecycle" (function
            | Draft -> "draft" | Active -> "active" | AwaitingEvidence -> "awaitingEvidence"
            | AwaitingVerification -> "awaitingVerification" | Resolved -> "resolved" | Closed -> "closed" | Archived -> "archived")
            [ Draft; Active; AwaitingEvidence; AwaitingVerification; Resolved; Closed; Archived ]

    let hypothesisState =
        WireEnum.make "hypothesisState" (function
            | Proposed -> "proposed" | Plausible -> "plausible" | Supported -> "supported"
            | Weakened -> "weakened" | Falsified -> "falsified" | Superseded -> "superseded")
            [ Proposed; Plausible; Supported; Weakened; Falsified; Superseded ]

    let unknownState =
        WireEnum.make "unknownState" (function
            | Identified -> "identified" | Investigating -> "investigating" | UnknownResolved -> "resolved"
            | AcceptedResidual -> "acceptedResidual" | UnknownSuperseded -> "superseded")
            [ Identified; Investigating; UnknownResolved; AcceptedResidual; UnknownSuperseded ]

    let materiality =
        WireEnum.make "materiality" (function
            | Blocking -> "blocking" | Material -> "material" | NonMaterial -> "nonMaterial" | Informational -> "informational")
            [ Blocking; Material; NonMaterial; Informational ]

    let assumptionState =
        WireEnum.make "assumptionState" (function
            | ExplicitUnverified -> "explicitUnverified" | ProvisionallyAccepted -> "provisionallyAccepted"
            | AssumptionVerified -> "verified" | Rejected -> "rejected" | Expired -> "expired")
            [ ExplicitUnverified; ProvisionallyAccepted; AssumptionVerified; Rejected; Expired ]

    let contradictionState =
        WireEnum.make "contradictionState" (function
            | Detected -> "detected" | Triaged -> "triaged" | ContradictionInvestigating -> "investigating"
            | ContradictionResolved -> "resolved" | ContradictionAcceptedResidual -> "acceptedResidual"
            | ContradictionSuperseded -> "superseded")
            [ Detected; Triaged; ContradictionInvestigating; ContradictionResolved; ContradictionAcceptedResidual; ContradictionSuperseded ]

    let actionState =
        WireEnum.make "actionState" (function
            | ActionProposed -> "proposed" | ActionApproved -> "approved" | ActionInProgress -> "inProgress"
            | ActionCompleted -> "completed" | ActionInterpreted -> "interpreted" | ActionRejected -> "rejected"
            | ActionCancelled -> "cancelled")
            [ ActionProposed; ActionApproved; ActionInProgress; ActionCompleted; ActionInterpreted; ActionRejected; ActionCancelled ]

    let actionKind =
        WireEnum.make "actionKind" (function
            | Test -> "test" | Inspection -> "inspection" | Query -> "query" | ActionKind.Comparison -> "comparison"
            | Experiment -> "experiment")
            [ Test; Inspection; Query; ActionKind.Comparison; Experiment ]

    let causeState =
        WireEnum.make "causeState" (function
            | CauseProposed -> "proposed" | CauseConfirmed -> "confirmed" | CauseReopened -> "reopened"
            | CauseSuperseded -> "superseded")
            [ CauseProposed; CauseConfirmed; CauseReopened; CauseSuperseded ]

    let causeRole =
        WireEnum.make "causeRole" (function
            | RootCause -> "rootCause" | ContributingFactor -> "contributingFactor" | Trigger -> "trigger"
            | EnablingCondition -> "enablingCondition")
            [ RootCause; ContributingFactor; Trigger; EnablingCondition ]

    let correctiveActionState =
        WireEnum.make "correctiveActionState" (function
            | CorrectionProposed -> "proposed" | CorrectionApproved -> "approved" | CorrectionInProgress -> "inProgress"
            | Implemented -> "implemented" | AwaitingVerificationOfEffect -> "awaitingVerificationOfEffect"
            | VerifiedEffective -> "verifiedEffective" | VerificationFailed -> "verificationFailed"
            | ReworkRequired -> "reworkRequired" | CorrectionCancelled -> "cancelled")
            [ CorrectionProposed; CorrectionApproved; CorrectionInProgress; Implemented; AwaitingVerificationOfEffect
              VerifiedEffective; VerificationFailed; ReworkRequired; CorrectionCancelled ]

    let correctiveActionKind =
        WireEnum.make "correctiveActionKind" (function
            | Mitigation -> "mitigation" | Workaround -> "workaround" | Correction -> "correction" | Prevention -> "prevention")
            [ Mitigation; Workaround; Correction; Prevention ]

    let evidenceCategory =
        WireEnum.make "evidenceCategory" (function
            | DirectObservation -> "directObservation" | Measurement -> "measurement" | Log -> "log" | Document -> "document"
            | ExperimentResult -> "experimentResult" | Testimony -> "testimony" | ExternalReference -> "externalReference")
            [ DirectObservation; Measurement; Log; Document; ExperimentResult; Testimony; ExternalReference ]

    let verificationStatus =
        WireEnum.make "verificationStatus" (function
            | Unverified -> "unverified" | PartiallyVerified -> "partiallyVerified" | Verified -> "verified"
            | Invalidated -> "invalidated")
            [ Unverified; PartiallyVerified; Verified; Invalidated ]

    let relationshipKind =
        WireEnum.make "relationshipKind" (function
            | Supports -> "supports" | Contradicts -> "contradicts" | Discriminates -> "discriminates"
            | Insufficient -> "insufficient")
            [ Supports; Contradicts; Discriminates; Insufficient ]

    let predictionStrength =
        WireEnum.make "predictionStrength" (function
            | Necessary -> "necessary" | Expected -> "expected" | DiscriminatingPrediction -> "discriminating"
            | NonDiscriminating -> "nonDiscriminating")
            [ Necessary; Expected; DiscriminatingPrediction; NonDiscriminating ]

    let predictionTiming =
        WireEnum.make "predictionTiming" (function Prospective -> "prospective" | Retrospective -> "retrospective") [ Prospective; Retrospective ]

    let verificationOutcome =
        WireEnum.make "verificationOutcome" (function Satisfied -> "satisfied" | Failed -> "failed" | Inconclusive -> "inconclusive")
            [ Satisfied; Failed; Inconclusive ]

    let conclusionStrength =
        WireEnum.make "conclusionStrength" (function
            | PlausibleExplanation -> "plausibleExplanation" | LeadingExplanation -> "leadingExplanation"
            | SupportedCause -> "supportedCause" | ConfirmedCause -> "confirmedCause"
            | AcceptedWithResidualUncertainty -> "acceptedWithResidualUncertainty")
            [ PlausibleExplanation; LeadingExplanation; SupportedCause; ConfirmedCause; AcceptedWithResidualUncertainty ]

    let proposalKind =
        WireEnum.make "proposalKind" (function ProposedHypothesis -> "hypothesis" | ProposedUnknown -> "unknown" | ProposedAction -> "action")
            [ ProposedHypothesis; ProposedUnknown; ProposedAction ]

    let proposalState =
        WireEnum.make "proposalState" (function ProposalActive -> "active" | ProposalAccepted -> "accepted" | ProposalRejected -> "rejected")
            [ ProposalActive; ProposalAccepted; ProposalRejected ]

    /// The engine records the hypothesis command name in HypothesisChange.Command (`$"{command}"`).
    let hypothesisCommand =
        WireEnum.make "hypothesisCommand" (function
            | MarkPlausible -> "markPlausible" | MarkSupported -> "markSupported" | MarkWeakened -> "markWeakened"
            | Falsify -> "falsify" | Supersede -> "supersede" | Reopen -> "reopen")
            [ MarkPlausible; MarkSupported; MarkWeakened; Falsify; Supersede; Reopen ]

    /// Engine string for a hypothesis command, exactly as `Decide` records it.
    let hypothesisCommandEngineName (c: HypothesisCommand) =
        match c with
        | MarkPlausible -> "MarkPlausible" | MarkSupported -> "MarkSupported" | MarkWeakened -> "MarkWeakened"
        | Falsify -> "Falsify" | Supersede -> "Supersede" | Reopen -> "Reopen"

    /// LifecycleChanged.command values the engine emits (Decide.lifecycleCommand and the
    /// RecordVerification follow-on). Engine string <-> wire string.
    let lifecycleCommands =
        [ "ActivateInvestigation", "activateInvestigation"
          "MarkAwaitingEvidence", "markAwaitingEvidence"
          "MarkAwaitingVerification", "markAwaitingVerification"
          "ResolveInvestigation", "resolveInvestigation"
          "CloseInvestigation", "closeInvestigation"
          "ReopenInvestigation", "reopenInvestigation"
          "VerificationFailed", "verificationFailed" ]

    let actorKinds = [ "human"; "agent"; "system" ]
    let contentKinds = [ "text"; "artifact" ]
