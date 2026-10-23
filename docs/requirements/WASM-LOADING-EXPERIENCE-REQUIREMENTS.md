# WebAssembly Loading Experience Requirements

Status: normative application UX requirements for Causa and subsequent Indy Init builds.

## WASM-LOAD-001: Initial loading screen
The application MUST render an accessible, branded loading screen immediately, before the F#/Limen WebAssembly runtime is initialized and interactive. The screen MUST be implemented in the initial HTML/CSS shell so that it does not depend on successful WebAssembly startup. Do not display a blank page while the runtime loads.

## WASM-LOAD-002: Loading icon
Provide a recognizable Causa application icon/mark on the loading screen, with an accessible text equivalent. A subtle CSS-based loading indicator MAY accompany it; honor prefers-reduced-motion. Do not require JavaScript animation or a separate animation library.

## WASM-LOAD-003: Clear states
Distinguish downloading/initializing, ready, and failed-to-load states. Only show percentage progress when measured against real runtime progress; otherwise use indeterminate progress and descriptive text (e.g. "Starting Causa…"). Never display fabricated progress.

## WASM-LOAD-004: Handoff
Keep the loading shell visible until the WebAssembly runtime and required application initialization have completed and the interactive UI is ready. Transition without a blank frame or duplicated controls. Do not mark ready merely because the .wasm download completed.

## WASM-LOAD-005: Failure and recovery
On timeout or initialization failure, show a human-readable error and a functional Retry action that reattempts initialization or safely reloads. Never leave the spinner indefinitely. Avoid exposing sensitive implementation details in user-visible errors.

## WASM-LOAD-006: Accessibility and responsive behavior
The loading experience MUST work on iPad, mobile, and desktop; provide readable contrast, keyboard-operable retry, appropriate status announcements, and reduced-motion support. Avoid focus loss during handoff.

## WASM-LOAD-007: Asset and branding requirements
Provide a reusable Causa icon suitable for the loading screen, favicon, and app identity. Prefer local/versioned assets that are available before WebAssembly loads; provide a text fallback if the icon fails. No remote third-party dependency may be required for the first render.

## WASM-LOAD-008: Acceptance
Verify via automated browser checks: shell appears before delayed WebAssembly initialization; correct ready handoff; no false progress; simulated download/init failure shows recovery; retry works; reduced-motion, keyboard, accessibility and responsive checks pass. Include a deployment-level check of the actual published build. Preserve the exact verified SHA and acceptance-gate requirements in DEFECT-CLASS-CLOSURE-AND-RELEASE-GATES.md.

## Implementation
Place implementation work items in the target repository's documented Praxis input folder, not GitHub Issues. This document adds requirements only; it does not assert that the current deployed rehearsal has implemented them.
