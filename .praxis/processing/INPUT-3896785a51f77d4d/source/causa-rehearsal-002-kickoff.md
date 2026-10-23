# Causa Rehearsal 002 — kickoff brief (human input)

Received 2026-10-23T12:14Z from the repository owner. Source material, not canonical state.

Mission: build Causa from a clean repository using the newest verified, mutually compatible Echelon
components; functional, visually complete, accessible, deployable; investigation data held in an
external GitHub repository; measurable engineering evidence.

Authoritative requirements: kemiller2002/Indy-init (materialized under docs/requirements/ at b5d9d79),
Causa storage contract v1.0.0 (kemiller2002/causa-data tag causa-storage-v1.0.0).
External investigation data: kemiller2002/causa-data. Historical comparison: kemiller2002/indy-init-rehearsal-001 (read-only).

Phases: 0 stack discovery/verification (blocking) → 1 bootstrap (make build/run/test/validate/package/deploy)
→ 2 external-data architecture (zero bundled investigations; explicit repo/branch/workspace + scoped
credential; validate + replay via F#; optimistic concurrency; independent remote verification)
→ 3 Causa visual implementation (Forma tokens; navy #14283F, teal #087F8C, amber #E9A23B, coral #D66B58,
ivory #F5F3ED; UI-001..016; WASM loading) → 4 bounded parallel execution after interface freeze
→ 5 security/adversarial review → 6 exact-SHA acceptance and GitHub Pages deployment.

Rules: no GitHub Issues intake; preserve R001; no fabricated evidence; R002 external-data requirement
supersedes bundling frozen demonstration fixtures; record PASS/FAIL/BLOCKED/NOT_RUN honestly.
Instrument: start/finish, first build, first screen, first external load, first verified write, tokens
if measurable, agent executions, attempts, defects, rework, compatibility problems, acceptance results.
