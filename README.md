# Michael

A personal scheduling tool — *my cal*, if you will.

Michael is a self-hosted alternative to Calendly for managing appointment scheduling.

## Development

This project uses Nix flakes for development environment management.

### Prerequisites

- [Nix](https://nixos.org/) with flakes enabled
- [direnv](https://direnv.net/) (recommended)

### Getting Started

If you have direnv installed, the dev shell activates automatically when you `cd` into the project directory:

```sh
direnv allow
```

Otherwise, enter the dev shell manually:

```sh
nix develop
```

### Running CI Locally

This project uses [SelfCI](https://app.radicle.xyz/nodes/radicle.dpc.pw/rad%3Az2tDzYbAXxTQEKTGFVwiJPajkbeDU)
for local-first continuous integration. SelfCI runs CI checks on your machine —
no remote servers or GitHub Actions needed. It has native jujutsu support.

**Run CI checks before finishing work:**

```sh
selfci check             # lint, build, frontend, and tests in parallel
```

CI runs four parallel jobs: lint (treefmt), backend build (dotnet), frontend
build (Elm + Tailwind), and tests. Configuration lives in `.config/selfci/`.
