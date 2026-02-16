_: {
  projectRootFile = "flake.nix";

  programs.nixfmt = {
    enable = true;
    includes = [
      "*.nix"
      "nix/**/*.nix"
    ];
  };

  programs.fantomas = {
    enable = true;
    includes = [
      "src/**/*.fs"
      "src/**/*.fsx"
      "src/**/*.fsi"
      "tests/**/*.fs"
    ];
  };

  programs.elm-format = {
    enable = true;
    includes = [
      "src/**/*.elm"
    ];
  };

  programs.prettier = {
    enable = true;
    includes = [
      "src/**/*.json"
      "src/**/*.css"
      "src/**/*.html"
      "tests/e2e/**/*.ts"
      "*.json"
    ];
    excludes = [
      "spike/**"
      "build/**"
      "elm-stuff/**"
      "node_modules/**"
    ];
  };

  programs.deadnix = {
    enable = true;
    includes = [
      "*.nix"
      "nix/**/*.nix"
    ];
  };

  programs.statix = {
    enable = true;
    includes = [
      "*.nix"
      "nix/**/*.nix"
    ];
  };
}
