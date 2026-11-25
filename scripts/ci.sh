#!/usr/bin/env bash
#
# The quality gate for macOS, Linux and Git Bash. Same steps as scripts/ci.ps1, which holds the
# full description. Pass --dotnet or --web to run half.
#
#   1. dotnet format   2. dotnet build   3. dotnet test   4. eslint   5. tsc   6. next build

set -euo pipefail
cd "$(dirname "$0")/.."

run_dotnet=true
run_web=true
case "${1:-}" in
  --dotnet) run_web=false ;;
  --web) run_dotnet=false ;;
esac

step() {
  local name="$1"; shift
  printf '\n==> %s\n' "$name"
  local started=$SECONDS
  "$@"
  printf '    ok (%ss)\n' "$((SECONDS - started))"
}

if $run_dotnet; then
  step 'dotnet format' dotnet format Truckroll.slnx --verify-no-changes --verbosity quiet
  step 'dotnet build' dotnet build Truckroll.slnx --configuration Release --nologo --verbosity quiet
  step 'dotnet test' dotnet test --solution Truckroll.slnx --configuration Release --no-build
fi

if $run_web; then
  cd web
  step 'eslint' npm run lint --silent
  step 'tsc' npx tsc --noEmit
  step 'next build' npm run build --silent
  cd ..
fi

printf '\nQuality gate passed.\n'
