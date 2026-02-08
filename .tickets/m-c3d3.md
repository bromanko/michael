---
id: m-c3d3
status: in_progress
deps: []
links: []
created: 2026-02-08T19:15:47Z
type: feature
priority: 2
assignee: Brian Romanko
---
# Add NL spec files to describe Michael for LLM reconstruction

Create one or more natural-language specification documents (attractor-style) that fully describe Michael's product behavior, architecture, APIs, workflows, and constraints so an LLM could recreate the project independent of implementation language.


## Notes

**2026-02-08T19:18:24Z**

Created docs/nl-spec/README.md and docs/nl-spec/michael-unified-spec.md with attractor-style NL spec covering product behavior, architecture, API contracts, data model, workflows, config, CI, and DoD. Ran selfci check successfully.

**2026-02-08T19:20:54Z**

Split NL spec into focused docs: product, API, data+algorithms, frontend, runtime+delivery. Updated docs/nl-spec/README.md with multi-file prompt and kept unified spec. Ran selfci check successfully after split.

**2026-02-08T19:23:49Z**

Added docs/nl-spec/ORDER.md defining split-spec precedence, conflict resolution, normative vs informative text, and acceptance gate. Updated docs/nl-spec/README.md to include ORDER.md in spec list and implementation prompt. Ran selfci check successfully.

**2026-02-08T19:35:20Z**

Resolved spec-gaps by tightening split NL specs: standardized error payload, full admin response contracts, duration policy (5-480), scheduling settings enforcement rules, booking-time 409 revalidation semantics, timezone/filter semantics, cookie contract, optional integration behavior, sync ranges, and cancellation email requirement in split specs. Created bug tickets from spec-writing phase: m-6cb5 (settings enforcement), m-cd77 (duration policy mismatch), plus note on existing m-19f2 (double-booking race). Ran selfci check successfully.
