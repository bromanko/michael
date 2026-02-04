{
  description = "Michael - a personal scheduling tool";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";
    flake-utils.url = "github:numtide/flake-utils";
    treefmt-nix.url = "github:numtide/treefmt-nix";
  };

  outputs =
    {
      self,
      nixpkgs,
      flake-utils,
      treefmt-nix,
      ...
    }:
    flake-utils.lib.eachDefaultSystem (
      system:
      let
        pkgs = nixpkgs.legacyPackages.${system};
        ticket = pkgs.callPackage ./nix/pkgs/ticket.nix { };
        python = pkgs.python3.withPackages (ps: [
          ps.anthropic
          ps.openai
          ps.google-genai
          ps.pydantic
        ]);
        treefmtEval = treefmt-nix.lib.evalModule pkgs ./treefmt.nix;
      in
      {
        formatter = treefmtEval.config.build.wrapper;

        checks.formatting = treefmtEval.config.build.check self;

        devShells.default = pkgs.mkShell {
          packages = [
            pkgs.dotnet-sdk_9
            pkgs.elmPackages.elm
            pkgs.elmPackages.elm-format
            pkgs.elmPackages.elm-test
            pkgs.elmPackages.elm-review
            pkgs.nodejs
            pkgs.nodePackages.tailwindcss
            pkgs.sqlite
            pkgs.statix
            pkgs.deadnix
            pkgs.overmind
            pkgs.tmux
            pkgs.inotify-tools
            pkgs.selfci
            python
            ticket
            treefmtEval.config.build.wrapper
          ];

          env = {
            ASPNETCORE_ENVIRONMENT = "Development";
            ASPNETCORE_URLS = "http://localhost:8000";
            MICHAEL_DB_PATH = "michael.db";
            MICHAEL_HOST_TIMEZONE = "America/New_York";
            MICHAEL_ADMIN_PASSWORD = "dev-password";
          };
        };
      }
    );
}
