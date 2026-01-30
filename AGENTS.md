# Michael — Agent Instructions

Michael (*my cal*) is a personal scheduling tool — a self-hosted Calendly
alternative. See `docs/design.md` for the full product design.

## Tech Stack

- **Backend**: F# with Falco
- **Frontend**: Elm (booking page and admin dashboard)
- **Styling**: Tailwind CSS
- **Database**: SQLite
- **Email**: Fastmail SMTP
- **Build/Dev**: Nix flakes with direnv

## Project Structure

This is a monorepo. The layout:

```
src/
  backend/            F# backend (Falco web framework, SQLite)
  frontend/
    booking/          Elm app — participant-facing booking page
    admin/            Elm app — host admin dashboard
    styles/           Tailwind CSS
spike/                Prototypes and spikes (not production code)
docs/                 Product design and spike results
nix/pkgs/             Custom Nix package definitions
build/                Build output (gitignored)
.tickets/             Task tickets (managed by `ticket` CLI)
```

## Version Control

This project uses **jujutsu (jj)**, not git. A `.jj` directory is present at
the repo root. Use `jj` commands for all version control operations:

- `jj status` — working copy status
- `jj log` — commit history
- `jj diff` — current changes
- `jj commit -m "message"` — commit changes
- `jj new` — start a new change

Do **not** use `git` commands directly.

## Development Environment

Enter the dev shell via direnv (automatic) or `nix develop`. All tooling
(dotnet, elm, tailwindcss, nodejs, sqlite, ticket) is provided by the flake.

## Task Management

Use `ticket` (aliased as `tk`) for tracking work. Tickets are stored as
markdown files in `.tickets/` and are committed to the repo.

```
tk create "title" -t feature -d "description"   # create a ticket
tk ls                                            # list open tickets
tk show <id>                                     # view a ticket (partial IDs work)
tk start <id>                                    # mark in-progress
tk close <id>                                    # mark closed
tk add-note <id> "text"                          # append a timestamped note
tk ready                                         # list tickets with deps resolved
tk blocked                                       # list tickets with unresolved deps
tk dep <id> <dep-id>                             # add a dependency
```

When starting work on a feature or bug, find or create a ticket first.

## Conventions

- Keep code simple and focused. Avoid over-engineering.
- Follow existing patterns in the codebase.
- Use Nix for all dependency management.
