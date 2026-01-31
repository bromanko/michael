#!/usr/bin/env bash
# Claude Code PreToolUse hook: block jj commit if build or tests fail.
# Receives tool input JSON on stdin. Only acts on jj commit commands.

set -euo pipefail

input=$(cat)
command=$(echo "$input" | jq -r '.tool_input.command // empty')

# Only intercept jj commit commands
if ! echo "$command" | grep -qE '^\s*jj\s+commit'; then
  exit 0
fi

echo "Pre-commit check: running build and tests..." >&2

cd "$(git rev-parse --show-toplevel)"

if ! make all 2>&1; then
  echo "BLOCKED: Build failed. Fix build errors before committing." >&2
  exit 2
fi

if ! make test 2>&1; then
  echo "BLOCKED: Tests failed. Fix test failures before committing." >&2
  exit 2
fi

echo "Pre-commit check passed." >&2
exit 0
