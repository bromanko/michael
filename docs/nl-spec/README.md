# Michael NL Specs

This directory contains Natural Language Specifications (NL Specs) for **Michael**.

These specs are intended to be directly consumable by an LLM coding agent so it can recreate the full project behavior and structure without needing the original source tree.

## Specs

Primary split specs:

- [Spec Order and Precedence](./ORDER.md)
- [Michael Product Specification](./michael-product-spec.md)
- [Michael API Specification](./michael-api-spec.md)
- [Michael Data and Algorithms Specification](./michael-data-and-algorithms-spec.md)
- [Michael Frontend Specification](./michael-frontend-spec.md)
- [Michael Runtime and Delivery Specification](./michael-runtime-and-delivery-spec.md)

Also included:

- [Michael Unified Specification](./michael-unified-spec.md) (single-file version)

## Recommended build prompt

Use a prompt like:

```text
Implement Michael exactly as described in:
- docs/nl-spec/ORDER.md
- docs/nl-spec/michael-product-spec.md
- docs/nl-spec/michael-api-spec.md
- docs/nl-spec/michael-data-and-algorithms-spec.md
- docs/nl-spec/michael-frontend-spec.md
- docs/nl-spec/michael-runtime-and-delivery-spec.md
```

If the implementer must choose a technology stack, default to the stack defined in the specification unless explicitly overridden.
