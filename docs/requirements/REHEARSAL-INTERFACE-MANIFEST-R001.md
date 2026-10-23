# R001 Source-Derived Interface Manifest for Next Rehearsal

Status: **EXTRACTED / NOT YET COMPILE-VERIFIED**. These symbols were read from kemiller2002/indy-init-rehearsal-001 default-branch source on 2026-10-23. This document is an interface baseline, NOT proof of compatible binary artifacts or a frozen new implementation.

## Source identity (Git blob SHAs)
- src/engine/Commands.fs: e9308672885b710cc8459bf1aa006bed7e79e92b
- src/engine/Decide.fs: da3a0d0706df27e32110d036e7e63fd859a9d123
- src/engine/Model.fs: da7099e2ebb87d4027bda727e485bfeab8245310
- src/engine/Sync.fs: 1b6d99330d416dd3407af7b6d1bb6f724f7a9e5f
- src/engine/Operational.fs: 36b339d3e0ce79df724c5480b739a566c9a0fe75
- src/engine/App.Engine.fsproj: bf6fad03f20ca3e2e1f37326bab5002dd279cc0a

Namespace: IndyInitRootCauseInvestigator.Engine

## Verified-by-source F# API signatures
```fsharp
type CommandEnvelope =
    { CommandId: Guid
      Investigation: InvestigationId
      ExpectedVersion: int64 option
      Actor: Actor
      RequestedAt: DateTimeOffset
      Command: Command }

type Event =
    { Sequence: int64
      Investigation: InvestigationId
      CommandId: Guid
      Actor: Actor
      At: DateTimeOffset
      Body: EventBody }

type DomainError =
    { Code: string
      Message: string
      Rule: string }

module Decide =
    val decide : Investigation option -> CommandEnvelope -> Result<EventBody list, DomainError list>
    val probe : Investigation -> Actor -> DateTimeOffset -> Command -> Result<unit, DomainError list>
    val execute : Investigation option -> CommandEnvelope -> Result<Event list, DomainError list>

module Evolve =
    val apply : Investigation option -> Event -> Investigation
    val replay : Event list -> Investigation option

module History =
    val validate : Event list -> DomainError list

module GitHub =
    val contentPath : InvestigationId -> string
    val readRequest : RemoteConfig -> InvestigationId -> HttpRequest
    val interpretRead : int -> string -> Result<(string * string) option, string>
    val interpretWrite : int -> string -> WriteOutcome

module Reconcile =
    val plan : Event list -> Event list -> Plan
```
Note: signatures of GitHub.readRequest/interpretRead/interpretWrite and Reconcile.plan are extracted from declarations; the `val` syntax above describes expected exposed signatures, not an independently compiled .fsi file.

## Source-derived state types
InvestigationLifecycle = Draft | Active | AwaitingEvidence | AwaitingVerification | Resolved | Closed | Archived.
HypothesisState = Proposed | Plausible | Supported | Weakened | Falsified | Superseded.
CausalAssertionState = CauseProposed | CauseConfirmed | CauseReopened | CauseSuperseded.
SyncStatus = LocalOnly | Pending | Sending | Synced | Conflicted of string | SyncUnknown.
WriteOutcome = Written of sha:string | RemoteConflict of detail:string | AuthFailed of detail:string | WriteFailed of detail:string.

## Invariants
- All authority transitions via Decide.decide / Decide.execute.
- History.validate re-derives command decisions and rejects mismatched event bodies, sequencing and actor/time/investigation inconsistencies. A raw Evolve.replay alone is NOT a history admission validator.
- ConfirmCause applies to CausalAssertion, not HypothesisState. COMMAND-EVENT-CATALOG.md was corrected accordingly.
- SyncStatus.Synced only after provider acknowledgment; offline and conflict remain separate.
- Frozen storage v1.0.0 is wire authority; this R001 source baseline is not permission to change frozen schema.

## Required remaining freeze steps
1. Verify repository commit SHA for all source blobs and extract complete exported F# signatures including type aliases and WASM JS interop boundary.
2. Compile exact baseline against pinned SDK and packages, run source/API compatibility tests.
3. Run all five causa-data golden fixture replays and negative histories through History.validate.
4. Verify JSON/WASM request/response serialization and sync error behavior.
5. Capture interface-manifest SHA and producer/consumer signoff.
6. If the baseline and frozen contract disagree, resolve explicitly and re-run validation. Until then status is EXTRACTED, not FROZEN.

## Owner contract
Domain owns these signatures; UI and persistence agents consume without editing. Any change is a versioned coordinator-approved revision with tests.
