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
        mailpit = pkgs.callPackage ./nix/pkgs/mailpit.nix { };
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
            pkgs.atlas
            pkgs.sqlite
            pkgs.statix
            pkgs.deadnix
            pkgs.overmind
            pkgs.tmux
            pkgs.inotify-tools
            mailpit
            pkgs.selfci
            pkgs.playwright-driver.browsers
            python
            ticket
            treefmtEval.config.build.wrapper
          ];

          env = {
            PLAYWRIGHT_BROWSERS_PATH = "${pkgs.playwright-driver.browsers}";
            PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD = "1";
            ASPNETCORE_ENVIRONMENT = "Development";
            ASPNETCORE_URLS = "http://localhost:8000";
            MICHAEL_DB_PATH = "michael.db";
            MICHAEL_HOST_TIMEZONE = "America/Los_Angeles";
            MICHAEL_ADMIN_PASSWORD = "dev-password";
            MICHAEL_CSRF_SIGNING_KEY = "dev-csrf-signing-key-at-least-32chars!";
            MICHAEL_CALDAV_FASTMAIL_URL = "http://localhost:9876/dav/calendars/user/fake@example.com";
            MICHAEL_CALDAV_FASTMAIL_USERNAME = "fake@example.com";
            MICHAEL_CALDAV_FASTMAIL_PASSWORD = "fake";
            MICHAEL_CALDAV_WRITEBACK_CALENDAR_URL = "http://localhost:9876/dav/calendars/user/fake@example.com/work/";
            FAKE_CALDAV_SCENARIO = "scenarios/busy-workday.json";
            FAKE_CALDAV_TIMEZONE = "America/Los_Angeles";
            MICHAEL_SMTP_HOST = "localhost";
            MICHAEL_SMTP_PORT = "1025";
            MICHAEL_SMTP_TLS = "false";
            MICHAEL_SMTP_FROM = "michael@localhost";
            MICHAEL_SMTP_FROM_NAME = "Michael (dev)";
            MICHAEL_PUBLIC_URL = "http://localhost:8000";
            MICHAEL_HOST_EMAIL = "host@example.com";
            MICHAEL_HOST_NAME = "Dev Host";
            FAKE_CALDAV_PORT = "9876";
          };
        };
      }
    );
}
