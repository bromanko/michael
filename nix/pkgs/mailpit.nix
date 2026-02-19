{
  lib,
  stdenvNoCC,
  fetchurl,
  autoPatchelfHook,
}:

let
  version = "1.29.1";

  sources = {
    "x86_64-linux" = {
      url = "https://github.com/axllent/mailpit/releases/download/v${version}/mailpit-linux-amd64.tar.gz";
      hash = "sha256-5TyfKcIIh36ajSfedGbuTjJb9TRyfQgNXvUX9mHUiQc=";
    };
    "aarch64-linux" = {
      url = "https://github.com/axllent/mailpit/releases/download/v${version}/mailpit-linux-arm64.tar.gz";
      hash = "sha256-yLlEtHUS6yMksREMQiCDxFxivjjeSzW6VY87hEeEi00=";
    };
    "aarch64-darwin" = {
      url = "https://github.com/axllent/mailpit/releases/download/v${version}/mailpit-darwin-arm64.tar.gz";
      hash = "sha256-cZnSPo+0ijDXNjn4z04PE25smX8ovJeLXsvF/s3mb/k=";
    };
    "x86_64-darwin" = {
      url = "https://github.com/axllent/mailpit/releases/download/v${version}/mailpit-darwin-amd64.tar.gz";
      hash = "sha256-+74VwmYhh2DWtJh1DF99xMpHXpPKcCtA1E31968fNS0=";
    };
  };

  src =
    sources.${stdenvNoCC.hostPlatform.system}
      or (throw "Unsupported platform: ${stdenvNoCC.hostPlatform.system}");
in
stdenvNoCC.mkDerivation {
  pname = "mailpit";
  inherit version;

  src = fetchurl {
    inherit (src) url hash;
  };

  sourceRoot = ".";

  nativeBuildInputs = lib.optionals stdenvNoCC.hostPlatform.isLinux [ autoPatchelfHook ];

  installPhase = ''
    runHook preInstall
    install -Dm755 mailpit $out/bin/mailpit
    runHook postInstall
  '';

  meta = {
    description = "Email and SMTP testing tool with API for developers";
    homepage = "https://github.com/axllent/mailpit";
    license = lib.licenses.mit;
    platforms = builtins.attrNames sources;
    mainProgram = "mailpit";
  };
}
