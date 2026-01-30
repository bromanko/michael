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
