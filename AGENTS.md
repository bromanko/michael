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

Enter the dev shell via direnv (automatic) or `nix develop`. The flake provides
`jj` and `ticket` (task management) in the dev shell.

## Conventions

- Keep code simple and focused. Avoid over-engineering.
- Follow existing patterns in the codebase.
- Use Nix for all dependency management.
