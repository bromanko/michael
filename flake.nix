{
  description = "Michael - a personal scheduling tool";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = {
    nixpkgs,
    flake-utils,
    ...
  }:
    flake-utils.lib.eachDefaultSystem (
      system: let
        pkgs = nixpkgs.legacyPackages.${system};
        ticket = pkgs.callPackage ./nix/pkgs/ticket.nix {};
        python = pkgs.python3.withPackages (ps: [
          ps.anthropic
          ps.openai
          ps.google-genai
          ps.pydantic
        ]);
      in {
        devShells.default = pkgs.mkShell {
          packages = [
            pkgs.dotnet-sdk_9
            pkgs.elmPackages.elm
            pkgs.elmPackages.elm-format
            pkgs.elmPackages.elm-test
            pkgs.nodejs
            pkgs.nodePackages.tailwindcss
            pkgs.sqlite
            python
            ticket
          ];
        };
      }
    );
}
