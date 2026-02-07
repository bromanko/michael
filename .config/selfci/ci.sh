#!/usr/bin/env bash
set -eou pipefail

function job_build() {
  selfci step start "dotnet restore"
  if ! dotnet restore src/backend/Michael.fsproj --verbosity quiet; then
    selfci step fail
  fi

  selfci step start "dotnet build"
  if ! dotnet build src/backend/Michael.fsproj --no-restore --verbosity quiet; then
    selfci step fail
  fi

  selfci step start "dotnet build fake-caldav"
  if ! dotnet build src/fake-caldav/FakeCalDav.fsproj --verbosity quiet; then
    selfci step fail
  fi
}

function job_frontend() {
  selfci step start "elm booking"
  if ! (cd src/frontend/booking && elm make src/Main.elm --output=/dev/null); then
    selfci step fail
  fi

  selfci step start "elm admin"
  if ! (cd src/frontend/admin && elm make src/Main.elm --output=/dev/null); then
    selfci step fail
  fi

  selfci step start "tailwind"
  if ! tailwindcss -i src/frontend/styles/booking.css -o /dev/null --minify 2>&1; then
    selfci step fail
  fi

  selfci step start "elm-review booking"
  if ! (cd src/frontend/booking && elm-review); then
    selfci step fail
  fi

  selfci step start "elm-review admin"
  if ! (cd src/frontend/admin && elm-review); then
    selfci step fail
  fi

  selfci step start "elm-test booking"
  if ! (cd src/frontend/booking && npx elm-test); then
    selfci step fail
  fi
}

function job_lint() {
  selfci step start "treefmt"
  if ! treefmt -q --fail-on-change; then
    selfci step fail
  fi
}

function job_test() {
  selfci step start "dotnet test"
  if ! dotnet run --project tests/Michael.Tests --verbosity quiet; then
    selfci step fail
  fi
}

case "$SELFCI_JOB_NAME" in
  main)
    selfci job start "lint"
    selfci job start "build"
    selfci job start "frontend"

    # Wait for build before running tests
    selfci job wait "build"
    selfci job start "test"

    selfci job wait "lint"
    selfci job wait "frontend"
    selfci job wait "test"
    ;;

  build)
    job_build
    ;;

  frontend)
    job_frontend
    ;;

  lint)
    job_lint
    ;;

  test)
    job_test
    ;;

  *)
    echo "Unknown job: $SELFCI_JOB_NAME"
    exit 1
    ;;
esac
