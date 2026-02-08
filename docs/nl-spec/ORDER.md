# Michael NL Spec Order and Precedence

This document defines how to consume the split NL specs when recreating Michael.

## Required Input Set

Use these files together:

1. `michael-product-spec.md`
2. `michael-api-spec.md`
3. `michael-data-and-algorithms-spec.md`
4. `michael-frontend-spec.md`
5. `michael-runtime-and-delivery-spec.md`

The unified file (`michael-unified-spec.md`) is a convenience mirror, not the source of precedence.

---

## Precedence Rules

When requirements appear to overlap or conflict, resolve in this strict order:

1. **`michael-api-spec.md`** (external contract authority)
2. **`michael-data-and-algorithms-spec.md`** (domain model + algorithm authority)
3. **`michael-product-spec.md`** (user-visible behavior authority)
4. **`michael-frontend-spec.md`** (UI/interaction authority)
5. **`michael-runtime-and-delivery-spec.md`** (ops/build/runtime authority)

Rationale:

- API contract is the most compatibility-sensitive surface.
- Data and algorithm semantics determine correctness behind that API.
- Product behavior constrains intended UX and scope.
- Frontend details should not violate API/data contracts.
- Runtime/build rules should support, not redefine, API/product semantics.

---

## Conflict Resolution Procedure

If an implementer detects ambiguity:

1. Prefer the higher-precedence spec.
2. Preserve existing endpoint paths, JSON shapes, and validation semantics.
3. Preserve algorithmic behavior (slot computation, blockers, migration integrity).
4. Preserve user-visible flow constraints.
5. Record any interpretation choices in implementation notes.

---

## Normative vs Informative Text

Treat these as **normative**:

- endpoint methods/paths
- request/response field names and requiredness
- validation constraints
- algorithm steps and interval semantics
- required startup and fail-fast behavior

Treat these as **informative** unless explicitly marked required:

- explanatory rationale paragraphs
- examples and sample payload values
- suggested phrasing and UI tone

---

## Language/Stack Independence

These NL specs are implementation-language independent in principle.

However, if no alternative is requested, the default target is the current Michael baseline stack:

- Backend: F# / Falco
- Frontend: Elm
- Styling: Tailwind
- DB: SQLite

Equivalent behavior in another stack is acceptable if all normative requirements are preserved.

---

## Acceptance Gate

A recreation is acceptable only if:

1. All five split specs are satisfied under this precedence model.
2. Public and admin API contracts remain compatible.
3. Slot computation and calendar blocker semantics match spec.
4. Runtime startup/CI/test expectations are met.
