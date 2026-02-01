---
name: warn-fsharp-async
enabled: true
event: file
conditions:
  - field: file_path
    operator: regex_match
    pattern: \.fs$
  - field: new_text
    operator: regex_match
    pattern: async\s*\{
action: warn
---

**F# convention: use `task { }`, not `async { }`.**

This project uses .NET Task-based async throughout. Avoid `async { }` blocks
to prevent unnecessary Async-to-Task conversions. Use `task { }` instead.
