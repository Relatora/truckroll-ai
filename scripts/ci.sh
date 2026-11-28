#!/usr/bin/env bash
#
# The quality gate for macOS, Linux and Git Bash. Same steps as scripts/ci.ps1, which holds the
# full description. Pass --dotnet, --web or --mobile to run one part, or --e2e for the
# Playwright tests against the running stack (step 11, a run of its own).
#
#   1. dotnet format   2. dotnet build   3. dotnet test   4. eslint   5. tsc   6. next build
#   7. app eslint   8. app tsc   9. app tests   10. app export   11. playwright (--e2e only)

set -euo pipefail
cd "$(dirname "$0")/.."

run_dotnet=true
run_web=true
run_mobile=true
run_e2e=false
case "${1:-}" in
  --dotnet) run_web=false; run_mobile=false ;;
  --web) run_dotnet=false; run_mobile=false ;;
  --mobile) run_dotnet=false; run_web=false ;;
  --e2e) run_dotnet=false; run_web=false; run_mobile=false; run_e2e=true ;;
esac

step() {
  local name="$1"; shift
  printf '\n==> %s\n' "$name"
  local started=$SECONDS
  "$@"
  printf '    ok (%ss)\n' "$((SECONDS - started))"
}

if $run_dotnet; then
  step 'dotnet format' dotnet format Truckroll.slnx --verify-no-changes --verbosity quiet --exclude src/Truckroll.Data/Migrations
  step 'dotnet build' dotnet build Truckroll.slnx --configuration Release --nologo --verbosity quiet
  step 'dotnet test' dotnet test --solution Truckroll.slnx --configuration Release --no-build
fi

if $run_web; then
  cd web
  step 'eslint' npm run lint --silent
  step 'typegen' npx next typegen
  step 'tsc' npx tsc --noEmit
  step 'next build' npm run build --silent
  cd ..
fi

if $run_mobile; then
  cd mobile
  step 'app eslint' npm run lint --silent
  step 'app tsc' npm run typecheck --silent
  step 'app tests' npm test --silent
  step 'app export' npx expo export --platform web --output-dir dist
  cd ..
fi

if $run_e2e; then
  cd web
  step 'playwright' npm run e2e --silent
  cd ..
fi

printf '\nQuality gate passed.\n'
