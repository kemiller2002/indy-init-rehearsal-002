# Next Rehearsal: External Data Boundary

Status: NORMATIVE REQUIREMENTS, NOT YET IMPLEMENTED. Applies to next rehearsal, not retroactively to R001.

## Goal
The app must build and operate from a clean checkout with **zero bundled demonstration investigations**. It knows how to create, validate, replay, serialize, and persist Causa objects, but obtains actual investigation records only after the user explicitly connects an authorized external GitHub repository.

## Requirements
- EXT-DATA-001 [P0] The application source, test assets included in production, release artifacts, Pages deployment, and runtime bundle MUST NOT contain the five R001 demonstration investigation datasets, event histories, golden states, or other prepopulated investigation content. The data contract, schema definitions, type declarations, command/event catalog, migration rules, and synthetic test generators MAY be included; synthetic tests MUST NOT ship production records.
- EXT-DATA-002 [P0] A fresh launch MUST display an accessible empty/disconnected workspace and an explicit Connect data repository action. No implicit demo import or automatic fallback to bundled fixtures is allowed.
- EXT-DATA-003 [P0] The user MUST specify or explicitly select the repository and permitted branch/workspace, and provide a scoped credential through a secure connection flow. A token alone is not sufficient to infer a safe repository target.
- EXT-DATA-004 [P0] Credentials MUST never be committed, bundled, logged, embedded in URLs, exposed in error text, stored in localStorage, or written into domain event files. Prefer short-lived least-privilege tokens or GitHub App authorization. In-memory-only credentials require reauthorization after reload. Do not use an unrestricted personal access token by default.
- EXT-DATA-005 [P0] The GitHub provider MUST check repository and branch permissions, validate file paths and manifests, and load only schema-compatible data through the full contract validation and F# history-admissibility pipeline. Malformed, forged, unauthorized, or incompatible data MUST fail closed with safe diagnostics.
- EXT-DATA-006 [P0] No data or repository mutation occurs on connect or read. Creating a new investigation is an explicit user action; write/publish requires separately verified authorization, optimistic concurrency, and no overwrite of unread remote history.
- EXT-DATA-007 [P0] The UI MUST distinguish disconnected, connecting, authorized-empty, loading, incompatible, access-denied, offline, and conflict states. Never imply a successful connection merely because a token was supplied.
- EXT-DATA-008 [P0] The app MUST create new Causa v1 file sets from the authoritative schema and engine without copying a demo investigation. A new empty investigation MUST be command-created, schema-validated, replayable, and round-trip verified.
- EXT-DATA-009 [P0] Build and acceptance tests MUST demonstrate a clean install with zero production investigation fixtures, no preloaded investigation records, successful authorized retrieval from a separately populated test repository, denial without authorization, malformed-data refusal, and successful create/read/write/reload. The deployed exact SHA must be verified.
- EXT-DATA-010 [P0] R001 remains an immutable experimental baseline. The next rehearsal MUST measure external-data startup, authorization, read, write, and recovery independently. Do not claim an external provider PASS based on mocked requests alone.
- EXT-DATA-011 [P0] Preserve compatibility with the frozen Causa storage contract as a *reader/writer specification* without vendoring its demonstration records. Version the contract and fail closed on incompatible data.
- EXT-DATA-012 [P0] The data repository MUST be configurable and isolated from the application repository. Treat repository content as untrusted even when authenticated. Do not grant data access through publicly readable Pages assets.

## Acceptance
A clean production build contains no demo investigation IDs, titles, events, snapshots or fixture bundle; first launch has zero investigations; connection to an explicitly chosen authorized data repository reveals only the permitted records; disconnect removes remote access and cached sensitive display state as policy requires; write and conflict tests are backed by independent SHA reads.

## Implementation intake
Before implementation, inspect each target repo's Praxis documentation and place work items in its **documented Praxis input directory** using the supported grammar, never GitHub Issues. Do not guess an intake path.

## Design note
The token is an authorization mechanism, not a storage address. Explicit repository/branch/workspace selection and read/write capability checks are necessary.
