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
      in {
        devShells.default = pkgs.mkShell {
          packages = [
            pkgs.jujutsu
            ticket
          ];
        };
      }
    );
}
