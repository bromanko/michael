---
id: mwbp-c35d
status: closed
deps: []
links: []
created: 2026-01-30T21:55:30Z
type: improvement
priority: 2
assignee: Brian Romanko
---
# Add FSharp.SystemTextJson for native F# type serialization

Add the FSharp.SystemTextJson NuGet package and register JsonFSharpConverter alongside the NodaTime converter in Program.fs. This will give us native System.Text.Json support for F# Option, discriminated unions, and records — eliminating the need for manual null-checking patterns on CLIMutable DTOs (e.g. BookRequest.Phone/Description) and enabling idiomatic F# types in request/response models.

