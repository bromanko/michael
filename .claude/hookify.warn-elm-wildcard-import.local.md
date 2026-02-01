---
name: warn-elm-wildcard-import
enabled: true
event: file
conditions:
  - field: file_path
    operator: regex_match
    pattern: \.elm$
  - field: new_text
    operator: regex_match
    pattern: exposing\s*\(\s*\.\.\s*\)
action: warn
---

**Elm convention: use explicit imports, not `exposing (..)`.**

List each imported name explicitly. The only exception is `Msg(..)` in view
modules, since views need all message constructors.
