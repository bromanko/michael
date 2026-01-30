# Plan: Configure Linting and Formatting

## Overview

Set up treefmt-nix to provide a unified `treefmt` command that runs all
formatters and auto-fix linters, plus add remaining lint tools to the dev
shell. Scope is `src/` and root-level Nix files (excludes `spike/`).

## Languages & Tools

| Language | Formatter (treefmt) | Linter |
|----------|---------------------|--------|
| F# | Fantomas | (compiler warnings) |
| Elm | elm-format | elm-review |
| Nix | nixfmt (RFC style) | statix, deadnix (also in treefmt) |
| CSS/HTML/JSON | Prettier | — |

## Changes

### 1. Add treefmt-nix flake input

In `flake.nix`, add the `treefmt-nix` input:

```nix
inputs = {
  nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";
  flake-utils.url = "github:numtide/flake-utils";
  treefmt-nix.url = "github:numtide/treefmt-nix";
};
```

Wire it up using `treefmt-nix.lib.evalModule` and expose:
- `formatter` output (enables `nix fmt`)
- `checks.formatting` output (enables `nix flake check` to enforce formatting)

### 2. Create `treefmt.nix` config file

Extract treefmt configuration into a separate `treefmt.nix` at the repo root
(idiomatic for treefmt-nix projects). Contents:

```nix
{ pkgs, ... }:
{
  projectRootFile = "flake.nix";

  programs.nixfmt = {
    enable = true;
    package = pkgs.nixfmt-rfc-style;
  };

  programs.fantomas.enable = true;

  programs.elm-format.enable = true;

  programs.prettier.enable = true;

  programs.deadnix.enable = true;

  programs.statix.enable = true;
}
```

Each formatter gets `includes` globs scoped to avoid `spike/`.

### 3. Add tools to dev shell

Add to `devShells.default.packages`:
- `elmPackages.elm-review` — Elm linter (requires separate initialization)
- `treefmtEval.config.build.wrapper` — so `treefmt` works directly, not just
  via `nix fmt`

Fantomas, Prettier, statix, and deadnix are already available through the
treefmt wrapper, but standalone versions remain useful for editor integration.

### 4. Create .editorconfig

Add a minimal `.editorconfig` at the repo root:
- UTF-8, LF line endings
- 4-space indent for F# (with Fantomas-specific settings)
- 4-space indent for Elm
- 2-space indent for Nix, JSON, CSS, HTML

Fantomas reads its config from `.editorconfig` (not a `.fantomasrc` file).
F# settings use `fsharp_`-prefixed keys:

```ini
[*.{fs,fsx,fsi}]
indent_size = 4
fsharp_max_line_length = 100
```

### 5. Add Prettier config

Add `.prettierrc.json` with:
- 2-space indent, no tabs
- trailing commas

Add `.prettierignore` to skip `build/`, `spike/`, `elm-stuff/`,
`node_modules/`, `.jj/`.

### 6. Initialize elm-review (deferred)

elm-review requires a `review/` directory with an Elm project that defines
review rules. This should be set up when the first Elm source files are
written. For now, the tool is available in the dev shell but not yet
configured.

## Files Modified

- `flake.nix` — add treefmt-nix input, wire up formatter + checks outputs,
  add tools to dev shell
- `treefmt.nix` — new (treefmt-nix module config)
- `.editorconfig` — new (includes Fantomas F# settings)
- `.prettierrc.json` — new
- `.prettierignore` — new

## Verification

1. `nix flake check` — flake evaluates and formatting check passes
2. `nix develop` — dev shell loads with all tools available
3. `nix fmt` — runs treefmt across the repo; nixfmt formats `flake.nix` and
   `nix/**/*.nix`; statix and deadnix run on Nix files
4. Create temp test files to verify each formatter works through treefmt:
   - Write a temp `.fs` file under `src/` → run `nix fmt` → confirm Fantomas
     formats it
   - Write a temp `.elm` file under `src/` → run `nix fmt` → confirm
     elm-format formats it
   - Write a temp `.json` file under `src/` → run `nix fmt` → confirm
     Prettier formats it
   - Clean up temp files after verification
5. Run `statix check .` and `deadnix .` standalone to confirm they work
