# Causa UI Experience Requirements

Status: normative requirements for rehearsal integration and future Indy Init builds.
Scope: investigation dashboard, causal graph, timeline, command interface, evidence inspector, persistence feedback, search, comparison, reporting, recovery, accessibility, and onboarding.

## Architectural principles
- The F# domain engine is the sole authority for legal commands, state transitions, causal assertions, and verification. UI components render projections; they MUST NOT invent conclusions, confidence scores, or permitted actions.
- All displayed investigative facts MUST be traceable to immutable event history or explicitly marked as derived/presentation-only.
- The frozen Causa v1.0.0 dataset MUST be rendered without mutation. Editing requires an isolated working copy.
- Use Forma for consistent components, Limen for WebAssembly integration, and Folio for print/reporting where appropriate. No unnecessary framework dependencies.
- Support iPad-first touch, keyboard, mobile, and desktop. Loading behavior is separately specified in WASM-LOADING-EXPERIENCE-REQUIREMENTS.md.
- Screens MUST distinguish unavailable data from zero counts, unknown values, and false claims.

## UI-001: Investigation dashboard [P0]
On launch, show all five frozen demonstration investigations and user working copies. Each card/row MUST show title, lifecycle, last recorded activity, and derived counts of hypotheses, evidence, and unresolved contradictions where supported. Show severity and owner only if present in the domain contract; otherwise omit or explicitly label unassigned/unspecified. Show the next legal action only if provided by F# policy. Provide sort, selection, loading, empty and error states. Opening an investigation MUST preserve its identity and context.

## UI-002: Interactive causal graph [P1]
Render nodes and typed relationships for observations, hypotheses, evidence, contradictions, causal assertions, and contributing/root causes as represented in the domain projection. Clicking/tapping or keyboard-selecting a node MUST open its detail and provenance. Visually distinguish established, proposed, contradicted, and unresolved relationships using both labels/shapes and color. Graph layout MUST be deterministic for identical inputs, support zoom/pan and fit-to-view, and provide a fully accessible textual relationship list. No browser-generated causal inference or fabricated edges. Large graphs MUST remain usable without blocking input.

## UI-003: Investigation timeline [P0]
Show chronological event history with event type, actor/source where available, event timestamp, and domain version/sequence. Provide filtering by category and a detail view showing before/after semantic changes when derivable. Preserve immutable history; clearly distinguish event time from recorded time and order by canonical event sequence where timestamps conflict. Selecting a timeline item MUST link to affected domain entities when possible.

## UI-004: Contextual command interface [P0]
Render only policy-authorized commands returned by the F# engine for the current version. For prohibited actions, show a clear reason when the engine supplies one, without implying the action is available. Capture required command inputs with validated forms and confirmation for destructive or irreversible operations. Reject stale-version submissions, show domain fault messages safely, and avoid duplicate submission. Keyboard invocation and touch targets are required. UI MUST NOT implement a parallel policy engine.

## UI-005: Evidence inspector [P0]
Display source/provenance, capture/record timestamps, original evidence content or safe reference, interpretation, reliability/limitations where defined, and explicit links to supporting and contradicting hypotheses. Never alter or overwrite the original evidence when interpretations change. Sanitize untrusted text/URLs; clearly label missing provenance. Enable navigation from graph, timeline, and hypothesis comparison.

## UI-006: Save, sync and conflict status [P0]
Expose distinct states: clean, unsaved, saving locally, saved locally, queued offline, syncing remotely, remotely synced, conflict, failed. Only claim remote synchronization after confirmed provider acknowledgement. Show last successful sync time when known. On expected-version conflict, preserve local edits and provide a recoverable compare/retry flow; do not overwrite remote history. Frozen fixtures MUST remain read-only. Never expose tokens.

## UI-007: Search and filtering [P1]
Support search across investigation titles and, where indexed, observations, evidence, hypotheses, and findings. Provide filters for lifecycle, unresolved contradictions, dates, and severity only where severity exists. Show result counts, highlighted matched text without altering original records, clear filters, no-results guidance, and keyboard access. Search results MUST link to the correct entity. Do not index confidential working data in external services without explicit authorization.

## UI-008: Hypothesis comparison [P1]
Allow side-by-side comparison of at least two hypotheses, showing claim, current state, supporting evidence, contradicting evidence, assumptions, unknowns, and verification criteria when defined. Counts and labels MUST be projection-derived. Provide a stacked responsive alternative on narrow screens. Do not rank hypotheses or display numeric confidence unless the domain engine explicitly defines and supplies that semantics.

## UI-009: Folio reports and export [P1]
Provide interim and final investigation reports with title, lifecycle, chronology, findings, evidence provenance, supporting/contradicting hypotheses, causal conclusions, unresolved unknowns, corrective actions, and verification outcome when present. Include report generation timestamp and investigation/event version. Support print-friendly rendering and PDF via browser print or supported Folio output. Export MUST NOT invent a verified conclusion. Redact sensitive data according to authorization; provide an accessible print preview.

## UI-010: Empty, error and recovery states [P0]
Design specific recoverable states for no investigations, no evidence, unavailable projection, schema mismatch, invalid event history, offline network, expired authentication, failed sync, and conflict. Every error MUST describe what happened, what data was preserved, and a safe next step. Provide Retry only when it performs a real action. Invalid histories MUST fail closed rather than silently rendering partial authoritative state. Do not expose internal secrets.

## UI-011: Keyboard, accessibility and iPad usability [P0]
Support keyboard-only navigation, visible focus, skip-to-content, appropriate landmarks, semantic status announcements, labeled controls, non-color-only states, and minimum usable touch targets. Support iPad portrait/landscape, narrow mobile screens, desktop, 200% text reflow, reduced motion, and screen readers. No essential operation may require drag-and-drop or hover. Preserve focus and scroll context during transitions and WebAssembly handoff. Graphs require equivalent accessible textual interaction.

## UI-012: First-run guidance [P1]
Offer a dismissible, replayable guided tour using the frozen synthetic investigations. Explain observations, hypotheses, evidence, contradictions, causal conclusions, and verification in context. Users MUST be able to skip the tour and start working immediately. Tour progress MUST NOT mutate frozen data or block accessibility. Persist dismissal only with an appropriate non-sensitive preference store.

## UI-013: Cross-view consistency [P0]
Dashboard, graph, timeline, evidence inspector, comparison, and report MUST refer to the same investigation ID and authoritative aggregate version. On a successful command, refresh dependent views atomically from the new projection. Stale data MUST be identified, not silently mixed across versions. Deep links MUST resolve or fail with a useful recovery state.

## UI-014: Visual identity and navigation [P0]
Use consistent Causa naming, icon, typography, navigation hierarchy, page titles, favicon, and accessible status colors. The icon and startup behavior MUST satisfy WASM-LOADING-EXPERIENCE-REQUIREMENTS.md. Navigation MUST distinguish frozen demonstrations from writable copies and expose current investigation context.

## UI-016: Causa semantic color system [P0]
The interface MUST have a deliberate, visibly colored Causa identity, not an effectively monochrome black/gray/white presentation. Use these initial canonical palette values:

| Token role | Color | Hex | Intended use |
| --- | --- | --- | --- |
| Deep navy | Deep navy | `#14283F` | Primary navigation, masthead, structural emphasis |
| Teal | Teal | `#087F8C` | Primary investigation actions, active selection and positive workflow emphasis |
| Amber | Amber | `#E9A23B` | Unresolved unknowns, pending review and attention |
| Coral | Coral | `#D66B58` | Contradictions, material conflicts and critical investigation alerts |
| Soft ivory | Soft ivory | `#F5F3ED` | Main workspace surface and light backgrounds |

All colors MUST be mapped to named, semantic Forma theme tokens, not scattered hardcoded CSS declarations. Use Forma components for surfaces, controls, states and focus treatment. Define derived foreground, hover, disabled, border and dark-mode tokens only after measuring contrast; the five hex values are starting identity colors, not a guarantee of accessible foreground/background combinations. Ensure WCAG AA contrast for normal text (4.5:1), large text (3:1), and relevant non-text indicators (3:1). Do not convey lifecycle, confidence, danger, verification or sync status through color alone: pair color with text and/or icons, and distinguish causal semantics from decorative accents. Use the same token semantics across dashboard, investigation views, timeline, causal graph, evidence inspector, report and startup shell. Print/Folio output MUST remain legible in grayscale.

Acceptance MUST include actual screenshots from the deployed exact SHA at desktop, iPad portrait/landscape and mobile sizes, plus automated token presence and contrast checks, keyboard focus checks, reduced-motion checks and independent visual review. Verify that navy, teal, amber, coral and ivory appear in appropriate populated states; absence of data must not force a misleading status color. A grayscale-only interface is a failed UI-016 acceptance even if functional tests pass.

## UI-015: Acceptance and traceability [P0]
For each UI requirement, create tagged acceptance tests and a traceability map to underlying domain contracts. Verify the five frozen investigations load and replay, keyboard/touch paths, graph textual equivalent, timeline ordering, legal command enforcement, save/sync truthfulness, offline/conflict recovery, responsive reflow, report fidelity, and startup failure. Test on deployed exact SHA; mocked persistence MUST NOT be represented as live write verification. Missing verification is BLOCKED/UNKNOWN, not PASS.

## Delivery priority and boundaries
P0 features form the core usable experience and must be completed before calling the integrated rehearsal UI accepted. P1 features are competition demonstration differentiators and remain explicitly tracked until implemented. Priorities do not waive tests or silently remove scope.

## Praxis intake and implementation
Convert these into granular implementation work items under each affected repository's *documented* Praxis input folder and exact accepted grammar, never GitHub Issues. Confirm the folder and grammar by reading the repository guidelines; do not guess. This document defines requirements only, does not launch R002 or a new experiment, and does not assert deployed implementation.

## Required evidence
- Per-feature traceability to F# domain projection/command and test ID.
- Screenshots and keyboard/mobile/browser accessibility evidence.
- Frozen-fixture read-only and golden-projection validation.
- Sync/conflict and report accuracy evidence.
- Independent adversarial review of UI-domain authority boundaries.
- Exact deployed SHA and successful acceptance job conclusion.
